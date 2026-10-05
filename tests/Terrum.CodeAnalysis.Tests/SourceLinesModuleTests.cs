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

public sealed class SourceLinesModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SourceLinesModuleQueriesFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] { "SourceRange", "CharacterFacts", "SourceText", "SourceLines" };
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
            public int32 GetStreamingLineExtent(int32 line) {
                if (!TryGetSourceLineExtent(line, init var range)) { return -1; }
                return range.Start * 256 + range.Length;
            }
            public int32 GetStreamingPosition(int32 position) {
                if (!TryGetSourcePosition(position, init var line, init var character)) { return -1; }
                return line * 256 + character;
            }
            public int32 GetStreamingOffset(int32 line, int32 character) {
                if (!TryGetSourceOffset(line, character, init var offset)) { return -1; }
                return offset;
            }
            """, "source-lines-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [.. "seed".Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
        [
            "Terrum.CodeAnalysis.GetStreamingLineExtent",
            "Terrum.CodeAnalysis.GetStreamingPosition",
            "Terrum.CodeAnalysis.GetStreamingOffset",
        ]);

        var fixtures = new[] {
            "", "plain", "\r", "\n", "\r\n", "\r\r\n\n\r",
            "a\r\nb\rc\nd\u0085e\u2028f\u2029", "\uD800\r\n\uDC00\0\uD800\uDC00",
        };
        var maximumLineCount = fixtures.Max(static text => SourceText.From(text).Lines.Length);
        var maximumSourceLength = fixtures.Max(static text => text.Length);

        BoundProgram CreateConsumer(string body)
        {
            var consumerTree = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() { {{body}} }
                """, "source-lines-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            return ModuleCompiler.Link(consumer, producer);
        }

        var lineInputs = Enumerable.Range(-1, maximumLineCount + 2)
            .Concat(new[] { int.MaxValue })
            .ToArray();
        var lineConsumers = lineInputs
            .Select(static line => $"return GetStreamingLineExtent({line});")
            .Select(CreateConsumer)
            .ToArray();

        var positionInputs = Enumerable.Range(-1, maximumSourceLength + 3)
            .Concat(new[] { int.MaxValue })
            .ToArray();
        var positionConsumers = positionInputs
            .Select(static position => $"return GetStreamingPosition({position});")
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
            .Select(static input => $"return GetStreamingOffset({input.Line}, {input.Character});")
            .Select(CreateConsumer)
            .ToArray();

        foreach (var text in fixtures)
        {
            var source = SourceText.From(text);
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. text.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };

            for (var query = 0; query < lineInputs.Length; query++)
            {
                var line = lineInputs[query];
                var expected = line >= 0 && line < source.Lines.Length
                    ? PackRange(source.Lines[line].SpanIncludingLineBreak)
                    : -1;
                Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(lineConsumers[query],
                    [grant], (NativeRuntimePolicy)policy));
            }
            for (var query = 0; query < positionInputs.Length; query++)
            {
                var position = positionInputs[query];
                var expected = position < 0 || position > source.Length
                    ? -1
                    : PackPosition(source.GetLinePosition(position));
                Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(positionConsumers[query],
                    [grant], (NativeRuntimePolicy)policy));
            }
            for (var query = 0; query < offsetInputs.Count; query++)
            {
                var input = offsetInputs[query];
                var expected = source.TryGetOffset(input.Line, input.Character, out var offset)
                    ? offset
                    : -1;
                Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(offsetConsumers[query],
                    [grant], (NativeRuntimePolicy)policy));
            }
        }
    }

    private static int PackRange(TextSpan range) => range.Start * 256 + range.Length;

    private static int PackPosition(LinePosition position) => position.Line * 256 + position.Character;

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
