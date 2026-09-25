// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SourceLineComponentTests
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
    [InlineData("bool ok = TryReadSourceLine(-1, out var content, out var including); return content.Start;")]
    [InlineData("bool ok = TryReadSourceLine(-1, out var content, out var including); return including.Length;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, out var line, out var character); return line;")]
    [InlineData("bool ok = TryGetSourcePosition(-1, out var line, out var character); return character;")]
    public async Task FailedQueriesCannotPublishReadableOutputsAsync(string body)
    {
        var source = await ReadComponentsAsync(includeTests: false).ConfigureAwait(true);
        var compilation = Compilation.Create(SyntaxTree.Parse(source + $"int32 Main() {{ {body} }}"),
            [new HostBuffer("Source", PrimitiveType.Char16, [65], false)]);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
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

        var components = await ReadComponentsAsync(includeTests: true).ConfigureAwait(true);
        const string Harness = """
            int32 Main() {
                int32 first = CheckSourceLines();
                if (first != 0) { return first; }
                return CheckSourceLines();
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(components + Harness),
        [
            new HostBuffer("Source", PrimitiveType.Char16, [.. text.Select(unit => (int)unit)], false),
            new HostBuffer("ExpectedLines", PrimitiveType.Int32, lines.ToImmutable(), false),
            new HostBuffer("ExpectedPositions", PrimitiveType.Int32, positions.ToImmutable(), false),
        ]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var path = Path.Combine(RepositoryRoot(), "artifacts", $"source-lines-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast)).ConfigureAwait(true);
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
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> ReadComponentsAsync(bool includeTests)
    {
        var source = new StringBuilder("using Terrum;\nnamespace Terrum.CodeAnalysis;\n");
        var names = new[] { "SourceRange.terrum", "CharacterFacts.terrum", "SourceLines.terrum" };
        foreach (var name in includeTests ? [.. names, "SourceLineTests.terrum"] : names)
        {
            var path = Path.Combine(RepositoryRoot(), "self-host", name);
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            var tree = SyntaxTree.Parse(text, path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.Equal(text, tree.Root.ToFullString());

            // Host grants currently bind one snapshot; retain declarations under their shared namespace.
            foreach (var node in tree.Root.Children.OfType<SyntaxNode>())
            {
                if (node.DescendantTokens().First().Kind is not (TokenKind.UsingKeyword or TokenKind.NamespaceKeyword))
                {
                    _ = source.AppendLine(node.ToFullString());
                }
            }
        }
        return source.ToString();
    }

    private static string RepositoryRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
