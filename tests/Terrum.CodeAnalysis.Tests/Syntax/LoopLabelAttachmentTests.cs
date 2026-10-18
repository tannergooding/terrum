// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Syntax;

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
    [TestCaseSource(nameof(Boundaries))]
    public void HostedAndPrelexedLabelsUsePhysicalSourceLines(string loop, string gap, bool invalid)
    {
        var text = Source(loop, gap);
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.That(lexed.Diagnostics, Is.Empty);
        foreach (var tree in new[] { SyntaxTree.Parse(text), SyntaxTree.Parse(lexed) })
        {
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
            if (invalid)
            {
                var diagnostic = global::System.Linq.Enumerable.Single(tree.Diagnostics);
                Assert.That(diagnostic.Code, Is.EqualTo("TRM1012"));
                Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(text.IndexOf(':', StringComparison.Ordinal), 1)));
            }
            else
            {
                Assert.That(tree.Diagnostics, Is.Empty);
            }
        }
    }
    [TestCase("a: b: while (true) { break b; }")]
    [TestCase("a: b: for (;;) { break b; }")]
    [TestCase("a: b: do { break b; } while (false);")]
    [TestCase("a:\n")]
    [TestCase("a: /*\n*/")]
    public void StackedAndIncompleteLabelsRejectWithoutLosingSource(string body)
    {
        var text = $"void Main() {{ {body} }}";
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
        Assert.That(Compilation.Create(tree).HasErrors, Is.True);
    }

    [Test]
    public void NamedArgumentColonStillPermitsNewlineTrivia()
    {
        var tree = SyntaxTree.Parse("void Main() { F(value:\n42); }");
        Assert.That(tree.Diagnostics, Is.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerPreservesEveryLabelBoundaryAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
        var text = string.Join("\n", Boundaries.Select(row => Source((string)row[0], (string)row[1])));
        var source = SourceText.From(text);
        Assert.That(source.Length <= ScannerHost.MaximumSourceLength, Is.True);
        var buffers = ScannerHost.CreateBuffers(source);
        var compilation = Compilation.Create(SyntaxTree.Parse(scanner + "int32 Main() { return Scan(); }"),
            buffers, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var native = NativeJit.ExecuteWithData(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), policy);
        var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
        Assert.That(adapted.Tokens.Select(token => (token.Kind, token.Span)), Is.EqualTo(Lexer.Lex(source).Tokens.Select(token => (token.Kind, token.Span))));
        var hosted = SyntaxTree.Parse(source);
        var actual = SyntaxTree.Parse(adapted);
        Assert.That(actual.Root.ToFullString(), Is.EqualTo(text));
        Assert.That(actual.Diagnostics.ToArray(), Is.EqualTo(hosted.Diagnostics.ToArray()));
        Assert.That(actual.Diagnostics.Count(item => item.Code == "TRM1012"), Is.EqualTo(Boundaries.Count(row => (bool)row[2])));

        ReadOnlySpan<byte> Buffer(int index) =>
            native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
    }

    internal static string Source(string loop, string gap) =>
        $"[NoThrow] int32 Main() {{ @const:{gap}{loop} return 42; }}";
}
