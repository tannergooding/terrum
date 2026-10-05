// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
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
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r\r\n\n\r")]
    [InlineData("a\r\nb\rc\nd\u0085e\u2028f\u2029")]
    [InlineData("\uD800\r\n\uDC00\0\uD800\uDC00")]
    [InlineData("\u000B\u000C\u001C\u001D\u001E\u001F\u00A0")]
    public async Task CompiledLinesAndEveryPositionAgreeWithSourceSnapshotAsync(string text)
    {
        await CheckAsync(text).ConfigureAwait(true);
    }

    [Fact]
    public async Task AlternatingBreaksAndSurrogatesPreserveAllOffsetsAsync()
    {
        await CheckAsync(string.Concat(Enumerable.Repeat("a\r\n\uD800\u2028\uDC00\n", 64))).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("bool ok = TryReadSourceLine(-1, init var content, init var including); return content.Start;")]
    [InlineData("bool ok = TryReadSourceLine(-1, init var content, init var including); return including.Length;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, init var line, init var character); return line;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, init var line, init var character); return character;")]
    [InlineData("bool ok = TryGetSourceOffset(-1, 0, init var offset); return offset;")]
    [InlineData("bool ok = TryGetSourceOffset(0, 2, init var offset); return offset;")]
    [InlineData("bool ok = TryGetSourceLineExtent(-1, init var extent); return extent.Start;")]
    [InlineData("bool ok = TryGetSourceLineExtent(1, init var extent); return extent.Length;")]
    [InlineData("bool ok = TryReadSourceCodeUnit(-1, init var codeUnit); return int32(codeUnit);")]
    [InlineData("bool ok = TryGetSourceRange(-1, 0, init var range); return range.Start;")]
    public async Task FailedQueriesCannotPublishReadableOutputsAsync(string body)
    {
        var trees = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var entry = SyntaxTree.Parse($"namespace Terrum.CodeAnalysis; int32 Main() {{ {body} }}", "entry.terrum");
        var compilation = Compilation.Create(trees.Add(entry),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        Assert.Contains(compilation.SourceDiagnostics,
            diagnostic => diagnostic.SyntaxTree == entry && diagnostic.Diagnostic.Code == "TRM3016");
    }

    [Fact]
    public void MultipleSnapshotsPreserveHostGrantRestrictionsAndDiagnosticOwnership()
    {
        var entry = SyntaxTree.Parse("int32 Main() { Write(); return 0; }", "entry.terrum");
        var write = SyntaxTree.Parse("void Write() { Host.Source[0] = char16(66); }", "write.terrum");
        var compilation = Compilation.Create([entry, write],
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Collection(compilation.SyntaxTrees, tree => Assert.Same(entry, tree), tree => Assert.Same(write, tree));
        Assert.NotEmpty(compilation.SourceDiagnostics);
        Assert.All(compilation.SourceDiagnostics, diagnostic => Assert.Same(write, diagnostic.SyntaxTree));
    }

    private static async Task CheckAsync(string text)
    {
        var source = SourceText.From(text);
        var lines = ImmutableArray.CreateBuilder<int>();
        foreach (var line in source.Lines)
        {
            lines.Add(line.Span.Start);
            lines.Add(line.Span.Length);
            lines.Add(line.SpanIncludingLineBreak.Length);
        }

        var positions = ImmutableArray.CreateBuilder<int>();
        for (var position = 0; position <= source.Length; position++)
        {
            var location = source.GetLinePosition(position);
            positions.Add(location.Line);
            positions.Add(location.Character);
        }

        var trees = await ReadComponentsAsync(includeTests: true).ConfigureAwait(true);
        const string Harness = """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                int32 first = CheckSourceLines();
                if (first != 0) { return first; }
                return CheckSourceLines();
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(trees.Add(SyntaxTree.Parse(Harness, "entry.terrum")),
            [
                new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false),
                new HostBuffer("ExpectedLines", PrimitiveType.Int32, lines.ToImmutable(), false),
                new HostBuffer("ExpectedPositions", PrimitiveType.Int32, positions.ToImmutable(), false),
            ], (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(0, NativeJit.Execute(program, policy));
            await ExecuteAsync(program, policy).ConfigureAwait(true);
        }
    }

    private static async Task ExecuteAsync(BoundProgram program, NativeRuntimePolicy policy, int expectedExit = 0)
    {
        var path = Path.Combine(RepositoryRoot(), "artifacts", $"source-lines-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Could not start compiled source-line component.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            Assert.Equal(expectedExit, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<ImmutableArray<SyntaxTree>> ReadComponentsAsync(bool includeTests)
    {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var names = new[]
        {
            "SourceRange.terrum", "CharacterFacts.terrum", "SourceText.terrum", "SourceLines.terrum",
        };
        foreach (var name in includeTests ? [.. names, "SourceLineTests.terrum"] : names)
        {
            var path = Path.Combine(RepositoryRoot(), "self-host", name);
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(text, path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.Equal(text, tree.Root.ToFullString());
            trees.Add(tree);
        }
        return trees.ToImmutable();
    }

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
