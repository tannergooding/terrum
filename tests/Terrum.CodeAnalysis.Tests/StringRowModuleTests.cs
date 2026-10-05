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

public sealed class StringRowModuleTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task StringRowModuleEncodesUtf8NamespaceRowsFromFreshSourceGrants(BoundRuntimePolicy policy)
    {
        var root = RepositoryRoot();
        var componentNames = new[] {
            "SourceRange", "CharacterFacts", "Utf16Scalar", "StringEscape", "StringLiteral", "StringRow",
        };
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
            public int32 ReadNamespaceRowByte(int32 position) {
                if (position < 0) { return -5; }
                if (!SourceRange.TryCreate(0, Host.Source.Length, init var spelling)) { return -1; }
                if (!StringLiteral.TryCreate(spelling, init var literal)) { return -2; }
                if (!TryGetStringNamespaceRowByteLength(literal, 1, init var byteLength)) { return -3; }
                if (!OwnedInt32Buffer.TryCreate(byteLength, out var workspace)) { return -4; }
                OwnedInt32Buffer row = EncodeStringNamespaceRow(literal, 1, move workspace);
                if (position >= row.Count) { return -1; }
                return row.Read(position);
            }
            """, "string-row-module.terrum");
        var producerCompilation = Compilation.Create(trees,
        [
            new HostBuffer("Source", PrimitiveType.Char16, [34, 120, 34], false) {
                IsInvocationScoped = true,
            },
        ], policy);
        Assert.False(producerCompilation.HasErrors,
            string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerCompilation.Program),
            ["Terrum.CodeAnalysis.ReadNamespaceRowByte"]);
        var fixtures = new[] {
            ("", "\"\""), ("Name", "\"Name\""), ("\u00E9\U0001F600", "\"\\u00E9\\U0001F600\""),
            ("A\u0301", "\"A\u0301\""),
        };
        var expectedRows = fixtures.Select(fixture =>
        {
            var section = TrRmStringTableWriter.EncodeTextRows([(fixture.Item1, 1)]);
            return new TrRmVariableTableIndex(section, 1, 8, 4).GetRow(1).ToArray();
        }).ToArray();
        var maximumRowLength = expectedRows.Max(static bytes => bytes.Length);
        var linked = new BoundProgram[maximumRowLength + 1];
        for (var position = 0; position < linked.Length; position++)
        {
            var consumerTree = SyntaxTree.Parse($$"""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return ReadNamespaceRowByte({{position}}); }
                """, "string-row-consumer.terrum");
            var consumerCompilation = Compilation.Create(consumerTree, ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            linked[position] = ModuleCompiler.Link(consumer, producer);
        }

        for (var fixtureIndex = 0; fixtureIndex < fixtures.Length; fixtureIndex++)
        {
            var spelling = fixtures[fixtureIndex].Item2;
            var grant = new HostBuffer("Source", PrimitiveType.Char16,
                [.. spelling.Select(static unit => (int)unit)], false) {
                IsInvocationScoped = true,
            };
            var expectedBytes = expectedRows[fixtureIndex];
            for (var position = 0; position < linked.Length; position++)
            {
                var expected = position < expectedBytes.Length ? expectedBytes[position] : -1;
                Assert.Equal(expected, NativeJit.ExecuteWithSourceGrant(linked[position], [grant],
                    (NativeRuntimePolicy)policy));
            }
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
