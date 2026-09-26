// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class SourceLineComponentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("\r\n")]
    [InlineData("\r\r\n\n\r")]
    [InlineData("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n")]
    [InlineData("a\r\nb\rc\nd\u0085e\u2028f\u2029")]
    [InlineData("\uD800\r\n\uDC00\0\uD800\uDC00")]
    [InlineData("\u000B\u000C\u001C\u001D\u001E\u001F\u00A0")]
    public async Task OwnedLineIndexMatchesEverySourcePositionAsync(string text)
    {
        var source = SourceText.From(text);
        var expected = ImmutableArray.CreateBuilder<int>();
        for (var position = 0; position <= source.Length; position++)
        {
            var location = source.GetLinePosition(position);
            expected.Add(location.Line);
            expected.Add(location.Character);
        }
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        const string Entry = """
            namespace Terrum.CodeAnalysis;
            int32 CheckIndex() {
                require SourceLineIndex.TryCreate(out var index);
                if (index.GetLineCount() != Host.LineCount[0]) { return 1; }
                if (index.TryGetPosition(-1, out var beforeLine, out var beforeCharacter)) { return 2; }
                if (index.TryGetPosition(Host.Source.Length + 1, out var afterLine, out var afterCharacter)) { return 3; }
                for (mut int32 position = 0; position <= Host.Source.Length; position = position + 1) {
                    require index.TryGetPosition(position, out var line, out var character);
                    if (line != Host.ExpectedPositions[position * 2]
                        || character != Host.ExpectedPositions[position * 2 + 1]) { return 4; }
                }
                return 0;
            }
            int32 Main() {
                int32 first = CheckIndex();
                if (first != 0) { return first; }
                return CheckIndex();
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(trees.Add(SyntaxTree.Parse(Entry, "index-entry.terrum")),
            [
                new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false),
                new HostBuffer("LineCount", PrimitiveType.Int32, [source.Lines.Length], false),
                new HostBuffer("ExpectedPositions", PrimitiveType.Int32, expected.ToImmutable(), false),
            ], (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("index.TryGetPosition(-1, out var line, out var character); return line;")]
    [InlineData("index.TryGetPosition(2, out var line, out var character); return character;")]
    [InlineData("SourceLineIndex copy = index; return copy.GetLineCount();")]
    [InlineData("SourceLineIndex moved = move index; return index.GetLineCount();")]
    public async Task OwnedLineIndexRejectsUnpublishedOutputsAndCopiesAsync(string body)
    {
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse("namespace Terrum.CodeAnalysis; int32 Main() { "
            + "require SourceLineIndex.TryCreate(out var index); " + body + " }", "invalid-index-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.SourceDiagnostics, diagnostic => diagnostic.SyntaxTree == entry);
    }

    private static async Task<ImmutableArray<SyntaxTree>> ReadIndexComponentsAsync()
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var path = Path.Combine(RepositoryRoot(), "self-host", "SourceLineIndex.terrum");
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var tree = SyntaxTree.Parse(text, path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
        return trees.Add(tree);
    }
}
