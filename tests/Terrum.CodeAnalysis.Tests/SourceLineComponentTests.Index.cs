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
        var extents = ImmutableArray.CreateBuilder<int>();
        var contentExtents = ImmutableArray.CreateBuilder<int>();
        foreach (var sourceLine in source.Lines)
        {
            extents.Add(sourceLine.SpanIncludingLineBreak.Start);
            extents.Add(sourceLine.SpanIncludingLineBreak.Length);
            contentExtents.Add(sourceLine.Span.Start);
            contentExtents.Add(sourceLine.Span.Length);
        }
        for (var position = 0; position <= source.Length; position++)
        {
            var location = source.GetLinePosition(position);
            Assert.True(source.TryGetOffset(location.Line, location.Character, out var offset));
            Assert.Equal(position, offset);
            expected.Add(location.Line);
            expected.Add(location.Character);
        }
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        const string Entry = """
            namespace Terrum.CodeAnalysis;
            int32 CheckIndex() {
                require SourceLineIndex.TryCreate(init var index);
                require SourceLineIndex.TryCreate(init var initialized);
                if (index.GetLineCount() != Host.LineCount[0]) { return 1; }
                if (initialized.GetLineCount() != index.GetLineCount()) { return 11; }
                if (initialized.TryGetPosition(-1, init var initLine, init var initCharacter)
                    || initialized.TryGetOffset(-1, 0, init var initOffset)
                    || initialized.TryGetLineExtent(-1, init var initExtent)
                    || initialized.TryGetLineContentExtent(-1, init var initContent)) { return 12; }
                require initialized.TryGetPosition(Host.Source.Length, init initLine, init initCharacter);
                require initialized.TryGetOffset(initLine, initCharacter, init initOffset);
                require initialized.TryGetLineExtent(initLine, init initExtent);
                require initialized.TryGetLineContentExtent(initLine, init initContent);
                if (initOffset != Host.Source.Length || initLine != Host.LineCount[0] - 1
                    || initExtent.Start != Host.ExpectedExtents[initLine * 2]
                    || initExtent.Length != Host.ExpectedExtents[initLine * 2 + 1]
                    || initContent.Start != Host.ExpectedContentExtents[initLine * 2]
                    || initContent.Length != Host.ExpectedContentExtents[initLine * 2 + 1]) { return 13; }
                if (index.TryGetPosition(-1, init var beforeLine, init var beforeCharacter)) { return 2; }
                if (index.TryGetPosition(Host.Source.Length + 1, init var afterLine, init var afterCharacter)) { return 3; }
                if (index.TryGetOffset(-1, 0, init var beforeOffset)
                    || index.TryGetOffset(Host.LineCount[0], 0, init var afterOffset)
                    || index.TryGetOffset(0, -1, init var negativeCharacter)
                    || index.TryGetOffset(0, int32.MaxValue, init var hugeCharacter)) { return 5; }
                if (index.TryGetLineExtent(-1, init var beforeExtent)
                    || index.TryGetLineExtent(Host.LineCount[0], init var afterExtent)
                    || index.TryGetLineExtent(int32.MaxValue, init var hugeExtent)
                    || index.TryGetLineContentExtent(-1, init var beforeContent)
                    || index.TryGetLineContentExtent(Host.LineCount[0], init var afterContent)
                    || index.TryGetLineContentExtent(int32.MaxValue, init var hugeContent)) { return 9; }
                for (mut int32 current = 0; current < Host.LineCount[0]; current = current + 1) {
                    require index.TryGetLineExtent(current, init var extent);
                    require index.TryGetLineContentExtent(current, init var content);
                    if (extent.Start != Host.ExpectedExtents[current * 2]
                        || extent.Length != Host.ExpectedExtents[current * 2 + 1]
                        || content.Start != Host.ExpectedContentExtents[current * 2]
                        || content.Length != Host.ExpectedContentExtents[current * 2 + 1]) { return 10; }
                }
                for (mut int32 position = 0; position <= Host.Source.Length; position = position + 1) {
                    require index.TryGetPosition(position, init var line, init var character);
                    if (line != Host.ExpectedPositions[position * 2]
                        || character != Host.ExpectedPositions[position * 2 + 1]) { return 4; }
                    if (!index.TryGetOffset(line, character, init var offset)
                        || offset != position) { return 6; }
                    if (position < Host.Source.Length
                        && Host.ExpectedPositions[(position + 1) * 2] != line
                        && index.TryGetOffset(line, character + 1, init var nextLineStart)) { return 7; }
                }
                require index.TryGetPosition(Host.Source.Length, init var lastLine, init var lastCharacter);
                if (index.TryGetOffset(lastLine, lastCharacter + 1, init var pastEnd)) { return 8; }
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
                new HostBuffer("ExpectedExtents", PrimitiveType.Int32, extents.ToImmutable(), false),
                new HostBuffer("ExpectedContentExtents", PrimitiveType.Int32, contentExtents.ToImmutable(), false),
            ], (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, policy));
            await ExecuteAsync(program, policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("plain", 1)]
    [InlineData("\n", 2)]
    [InlineData("a\r\n", 2)]
    [InlineData("\r\nb", 2)]
    [InlineData("\r\nx\n", 3)]
    [InlineData("\n\n\n\n", 5)]
    [InlineData("a\r\nb\rc\nd\u0085e\u2028f\u2029", 7)]
    public async Task OwnedLineIndexReservesOnlyActualStartsAsync(string text, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(text);
        var last = SourceText.From(text).GetLinePosition(text.Length);
        var trees = await ReadIndexComponentsWithCapacityAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceLineIndex.TryCreate(init var index);
                if (index.GetLineCount() != {{expectedCount}}
                    || index.GetCapacityForTest() != {{expectedCount}}) { return 1; }
                require index.TryGetPosition(Host.Source.Length, init var line, init var character);
                if (line != {{last.Line}} || character != {{last.Character}}) { return 2; }
                if (!index.TryGetOffset(line, character, init var offset)
                    || offset != Host.Source.Length) { return 3; }
                return 0;
            }
            """, "capacity-index-entry.terrum");
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(trees.Add(entry),
                [new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false)],
                (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task SparseLineIndexFitsAtMaximumSourceLengthAsync(NativeRuntimePolicy policy, bool fewLines)
    {
        const int MaximumCapacity = 1_048_576;
        var text = fewLines ? new string('a', MaximumCapacity - 3) + "\r\nb"
            : new string('a', MaximumCapacity);
        var trees = await ReadIndexComponentsWithCapacityAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse($$"""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!SourceLineIndex.TryCreate(init var index)) { return 1; }
                if (index.GetLineCount() != {{(fewLines ? 2 : 1)}}) { return 2; }
                if (index.GetCapacityForTest() != {{(fewLines ? 2 : 1)}}) { return 4; }
                require index.TryGetPosition(Host.Source.Length, init var line, init var character);
                if (line != {{(fewLines ? 1 : 0)}}
                    || character != {{(fewLines ? 1 : MaximumCapacity)}}) { return 3; }
                if (!index.TryGetOffset(line, character, init var offset)
                    || offset != Host.Source.Length
                    || index.TryGetOffset(line, int32.MaxValue, init var excessive)) { return 5; }
                return 0;
            }
            """, "sparse-index-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task DenseLineIndexHonorsExactBufferCapacityAsync(NativeRuntimePolicy policy, bool overflow)
    {
        const int MaximumCapacity = 1_048_576;
        var text = new string('\n', overflow ? MaximumCapacity : MaximumCapacity - 1);
        var trees = await ReadIndexComponentsWithCapacityAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse(overflow && policy == NativeRuntimePolicy.CheckedExceptions ? """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                unsafe {
                    try {
                        SourceLineIndex.TryCreate(init var index);
                        return 1;
                    } catch {
                        if (!OwnedInt32Buffer.TryCreate(1, out var raw)) { return 2; }
                        mut OwnedInt32Buffer next = move raw;
                        if (!next.TryAppend(42) || next.Read(0) != 42) { return 3; }
                        return 0;
                    }
                }
            }
            """ : overflow ? """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                SourceLineIndex.TryCreate(init var index);
                return 1;
            }
            """ : """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (!SourceLineIndex.TryCreate(init var index)) { return 1; }
                if (index.GetLineCount() != Host.Source.Length + 1) { return 2; }
                if (index.GetCapacityForTest() != Host.Source.Length + 1) { return 4; }
                require index.TryGetPosition(Host.Source.Length, init var line, init var character);
                if (line != Host.Source.Length || character != 0) { return 3; }
                if (!index.TryGetOffset(line, character, init var offset)
                    || offset != Host.Source.Length
                    || index.TryGetOffset(line - 1, 1, init var nextLineStart)) { return 5; }
                return 0;
            }
            """, "dense-index-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false)],
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), policy,
            overflow && policy == NativeRuntimePolicy.FailFast ? unchecked((int)0xE0540001) : 0)
            .ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OwnedLineIndexDoesNotRereadSourceAfterConstructionAsync(NativeRuntimePolicy policy)
    {
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        const string Entry = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                require SourceLineIndex.TryCreate(init var index);
                Host.Source[0] = char16(10);
                if (index.GetLineCount() != 1) { return 1; }
                for (mut int32 position = 0; position <= 3; position = position + 1) {
                    require index.TryGetPosition(position, init var line, init var character);
                    if (line != 0 || character != position) { return 2; }
                    if (!index.TryGetOffset(line, character, init var offset)
                        || offset != position) { return 3; }
                    require index.TryGetLineExtent(0, init var extent);
                    if (extent.Start != 0 || extent.Length != 3) { return 4; }
                }
                return 0;
            }
            """;
        var compilation = Compilation.Create(trees.Add(SyntaxTree.Parse(Entry, "index-snapshot-entry.terrum")),
            [new HostBuffer("Source", PrimitiveType.Char16, [97, 98, 99], true)], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        await ExecuteAsync(Assert.IsType<BoundProgram>(compilation.Program), policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("index.TryGetPosition(-1, init var line, init var character); return line;")]
    [InlineData("index.TryGetPosition(2, init var line, init var character); return character;")]
    [InlineData("index.TryGetOffset(-1, 0, init var position); return position;")]
    [InlineData("index.TryGetOffset(0, 2, init var position); return position;")]
    [InlineData("index.TryGetLineExtent(-1, init var extent); return extent.Start;")]
    [InlineData("index.TryGetLineExtent(1, init var extent); return extent.Length;")]
    [InlineData("SourceLineIndex copy = index; return copy.GetLineCount();")]
    [InlineData("SourceLineIndex moved = move index; return index.GetLineCount();")]
    public async Task OwnedLineIndexRejectsUnpublishedOutputsAndCopiesAsync(string body)
    {
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse("namespace Terrum.CodeAnalysis; int32 Main() { "
            + "require SourceLineIndex.TryCreate(init var index); " + body + " }", "invalid-index-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.SourceDiagnostics, diagnostic => diagnostic.SyntaxTree == entry);
    }

    [Fact]
    public async Task OwnedLineIndexCannotReadOutputWithoutSuccessfulCreationAsync()
    {
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        var entry = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                SourceLineIndex.TryCreate(init var index);
                return index.GetLineCount();
            }
            """, "unpublished-index-entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [], false)]);
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

    private static async Task<ImmutableArray<SyntaxTree>> ReadIndexComponentsWithCapacityAsync()
    {
        var trees = await ReadIndexComponentsAsync().ConfigureAwait(true);
        var source = trees[^1].Source;
        var marker = source.Text.IndexOf("    [NoThrow]", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        var instrumented = source.Text.Insert(marker,
            "    public int32 GetCapacityForTest() { return _starts.Capacity; }\n\n");
        var tree = SyntaxTree.Parse(instrumented, source.Path);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        return trees.SetItem(trees.Length - 1, tree);
    }
}
