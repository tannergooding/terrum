// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LoopLabelAttachmentTests
{
    public static IEnumerable<object[]> Boundaries
    {
        get
        {
            foreach (var loop in new[] {
                "while (true) { break const; }",
                "do { break const; } while (false);",
                "for (;;) { break const; }",
            })
            {
                foreach (var (gap, invalid) in new[] {
                    ("", false), (" ", false), ("\t", false), ("/* gap */", false),
                    ("//** gap **//", false),
                    ("\r", true), ("\n", true), ("\r\n", true),
                    ("\u0085", true), ("\u2028", true), ("\u2029", true),
                    ("/*\n*/", true), ("/*\r\n*/", true), ("/*\u2028*/", true),
                    ("// gap\n", true), ("/// gap\n", true),
                })
                {
                    yield return [loop, gap, invalid];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void HostedAndPrelexedLabelsUsePhysicalSourceLines(string loop, string gap, bool invalid)
    {
        var text = Source(loop, gap);
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.Empty(lexed.Diagnostics);
        foreach (var tree in new[] { SyntaxTree.Parse(text), SyntaxTree.Parse(lexed) })
        {
            Assert.Equal(text, tree.Root.ToFullString());
            if (invalid)
            {
                var diagnostic = Assert.Single(tree.Diagnostics);
                Assert.Equal("TRM1012", diagnostic.Code);
                Assert.Equal(new TextSpan(text.IndexOf(':', StringComparison.Ordinal), 1), diagnostic.Span);
            }
            else
            {
                Assert.Empty(tree.Diagnostics);
            }
        }
    }

    [Theory]
    [InlineData("a: b: while (true) { break b; }")]
    [InlineData("a: b: for (;;) { break b; }")]
    [InlineData("a: b: do { break b; } while (false);")]
    [InlineData("a:\n")]
    [InlineData("a: /*\n*/")]
    public void StackedAndIncompleteLabelsRejectWithoutLosingSource(string body)
    {
        var text = $"void Main() {{ {body} }}";
        var tree = SyntaxTree.Parse(text);
        Assert.True(tree.HasErrors);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.True(Compilation.Create(tree).HasErrors);
    }

    [Fact]
    public void NamedArgumentColonStillPermitsNewlineTrivia()
    {
        var tree = SyntaxTree.Parse("void Main() { F(value:\n42); }");
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerPreservesEveryLabelBoundaryAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
        var text = string.Join("\n", Boundaries.Select(row => Source((string)row[0], (string)row[1])));
        var source = SourceText.From(text);
        Assert.True(source.Length <= ScannerHost.MaximumSourceLength);
        var buffers = ScannerHost.CreateBuffers(source);
        var compilation = Compilation.Create(SyntaxTree.Parse(scanner + "int32 Main() { return Scan(); }"),
            buffers, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var native = NativeJit.ExecuteWithData(Assert.IsType<BoundProgram>(compilation.Program), policy);
        var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
        Assert.Equal(Lexer.Lex(source).Tokens.Select(token => (token.Kind, token.Span)),
            adapted.Tokens.Select(token => (token.Kind, token.Span)));
        var hosted = SyntaxTree.Parse(source);
        var actual = SyntaxTree.Parse(adapted);
        Assert.Equal(text, actual.Root.ToFullString());
        Assert.Equal(hosted.Diagnostics.ToArray(), actual.Diagnostics.ToArray());
        Assert.Equal(Boundaries.Count(row => (bool)row[2]), actual.Diagnostics.Count(item => item.Code == "TRM1012"));

        ReadOnlySpan<byte> Buffer(int index) =>
            native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
    }

    internal static string Source(string loop, string gap) =>
        $"[NoThrow] int32 Main() {{ @const:{gap}{loop} return 42; }}";
}
