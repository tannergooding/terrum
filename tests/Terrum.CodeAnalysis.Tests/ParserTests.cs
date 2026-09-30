// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ParserTests
{
    [Fact]
    public void DiscardStatementRetainsItsDistinctShapeAndTrivia()
    {
        const string Source = "int32 Main() { _ /* destination */ = int32(42); return 42; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal("syntax-32", SyntaxTree.LanguageVersion);
        var discard = Assert.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.DiscardStatement);
        Assert.Equal(TokenKind.Underscore, Assert.IsType<SyntaxTokenElement>(discard.Children[0]).Token.Kind);
        Assert.Equal(TokenKind.Equals, Assert.IsType<SyntaxTokenElement>(discard.Children[1]).Token.Kind);
        Assert.Equal(SyntaxKind.InvocationExpression, Assert.IsType<SyntaxNode>(discard.Children[2]).Kind);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("mut class Leaf { public mut int32 Value; public Leaf(int32 value) { Value = value; } public mut void Set(int32 value) { Value = value; } }")]
    [InlineData("bool Create(out write ManagedNode node) { return ManagedNode.TryCreate(42, out node); }")]
    [InlineData("mut class Link { public mut write Link? Next; public Link() { Next = null; } }")]
    [InlineData("void Update(write ManagedNode node) { node.SetValue(42); }")]
    public void ParsesManagedWriteCapabilitiesWithoutLosingSource(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    [InlineData("char16")]
    public void TypeCallsPreserveTypeSpellingAndBindMoreTightlyThanAddition(string type)
    {
        var tree = SyntaxTree.Parse("void F() { return " + type + "(/* conversion */ value) + 1; }");
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var cast = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.InvocationExpression);
        Assert.Equal(type, Assert.IsType<SyntaxTokenElement>(
            Assert.IsType<SyntaxNode>(cast.Children[0]).Children[0]).Token.Text);
        var arguments = Assert.IsType<SyntaxNode>(cast.Children[1]);
        Assert.Equal(SyntaxKind.NameExpression, Assert.IsType<SyntaxNode>(
            Assert.IsType<SyntaxNode>(arguments.Children[1]).Children[0]).Kind);
        var addition = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.BinaryExpression);
        Assert.Same(cast, addition.Children[0]);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("void F() { require /* \u03B1 */ ready; }")]
    [InlineData("void F() { require TryRead(out var value) && value > 0; }")]
    public void ParsesRequirementsWithoutLosingTriviaOrSourceShape(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var requirement = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.RequireStatement);
        Assert.Equal(3, requirement.Children.Length);
        Assert.Equal(TokenKind.RequireKeyword, Assert.IsType<SyntaxTokenElement>(requirement.Children[0]).Token.Kind);
        Assert.Equal(TokenKind.Semicolon, Assert.IsType<SyntaxTokenElement>(requirement.Children[2]).Token.Kind);
        AssertLossless(tree);
    }

    [Fact]
    public void TryFinallyRetainsBothBlocksAndKeywordTrivia()
    {
        const string Source = "void F() { /* before */ try /* body */ { return; } /* keyword */ finally /* cleanup */ { ; } }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var region = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryFinallyStatement);
        Assert.Equal(4, region.Children.Length);
        Assert.Equal(TokenKind.TryKeyword, Assert.IsType<SyntaxTokenElement>(region.Children[0]).Token.Kind);
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(region.Children[1]).Kind);
        var keyword = Assert.IsType<SyntaxTokenElement>(region.Children[2]).Token;
        Assert.Equal(TokenKind.FinallyKeyword, keyword.Kind);
        Assert.Contains(keyword.LeadingTrivia, trivia => trivia.Text == "/* keyword */");
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(region.Children[3]).Kind);
        Assert.Equal(Source.IndexOf("try", StringComparison.Ordinal), region.Span.Start);
        Assert.Equal(Source.LastIndexOf("} }", StringComparison.Ordinal) + 1, region.Span.End);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("void F() { try { return; } return; }", TokenKind.FinallyKeyword, 2)]
    [InlineData("void F() { try { return; } finally return; }", TokenKind.OpenBrace, 2)]
    [InlineData("void F() { try finally { } return; }", TokenKind.OpenBrace, 1)]
    public void MalformedTryFinallyPreservesMissingSyntaxAndFollowingStatements(string source, TokenKind missingKind, int returns)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        var region = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryFinallyStatement);
        Assert.Equal(4, region.Children.Length);
        Assert.Contains(region.DescendantTokens(), token => token.Kind == missingKind && token.IsMissing
            && token.Text.Length == 0 && token.Span.Length == 0);
        Assert.Equal(returns, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ReturnStatement));
        AssertLossless(tree);
    }

    [Fact]
    public void UnsafeRecoveryRetainsBothRegionsOptionalCleanupAndKeywordTrivia()
    {
        const string Source = "void F() { unsafe /* scope */ { try /* body */ { require false; } /* recovery */ catch /* block */ { ; } /* last */ finally { ; } } }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var unsafeBlock = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.UnsafeStatement);
        Assert.Equal(TokenKind.UnsafeKeyword, Assert.IsType<SyntaxTokenElement>(unsafeBlock.Children[0]).Token.Kind);
        var scope = Assert.IsType<SyntaxNode>(unsafeBlock.Children[1]);
        var open = Assert.IsType<SyntaxTokenElement>(scope.Children[0]).Token;
        Assert.Contains(open.LeadingTrivia, trivia => trivia.Text == "/* scope */");
        var region = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryCatchStatement);
        Assert.Equal(6, region.Children.Length);
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(region.Children[1]).Kind);
        Assert.Contains(Assert.IsType<SyntaxTokenElement>(region.Children[2]).Token.LeadingTrivia,
            trivia => trivia.Text == "/* recovery */");
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(region.Children[3]).Kind);
        Assert.Equal(TokenKind.FinallyKeyword, Assert.IsType<SyntaxTokenElement>(region.Children[4]).Token.Kind);
        Assert.Equal(SyntaxKind.Block, Assert.IsType<SyntaxNode>(region.Children[5]).Kind);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("void F() { unsafe { try { } catch return; } return; }", TokenKind.OpenBrace)]
    [InlineData("void F() { unsafe { try { } catch { } finally return; } return; }", TokenKind.OpenBrace)]
    public void MalformedRecoveryPreservesMissingBlocksAndFollowingStatements(string source, TokenKind missingKind)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        var region = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryCatchStatement);
        Assert.Contains(region.DescendantTokens(), token => token.Kind == missingKind && token.IsMissing);
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ReturnStatement);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("")]
    [InlineData("// only trivia\r\n")]
    [InlineData("using Terrum; public static void Main() { Terminal.WriteLine(\"Hello, world!\"); }")]
    [InlineData("namespace Demo; int32 Add(int32 left, int32 right) { return left + right; }")]
    [InlineData("void F(Span<TextSpan> args) { mut int32 i = 0; while (i < args.Length) { if (i == 2) { return; } else { i = i + 1; } } }")]
    [InlineData("public struct Point { public int32 X; public int32 Y; public Point(int32 x, int32 y) { X = x; Y = y; } }")]
    [InlineData("void F(ref int32 a, mut ref int32 b, out int32 c) { var ok = T.TryParse(a, out var v); G(mut ref b, name: v); return (); }")]
    [InlineData("void F() { Namespace.List<Span<int32?>?>? values = Get(); var x = a[0].F(); }")]
    [InlineData("void F() { var point = new Point(3, 4); }")]
    public void ParsesSupportedSyntax(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("int32??")]
    [InlineData("T??")]
    [InlineData("T? /* gap */ ?")]
    [InlineData("int32?\r\n?")]
    [InlineData("List<int32??>?")]
    [InlineData("List<int32>??")]
    public void RejectsRepeatedOptionalModifiersWithoutLosingText(string type)
    {
        var text = "void F(" + type + " value) {} void G() {}";
        var tree = SyntaxTree.Parse(text);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("TRM1005", diagnostic.Code);
        var firstQuestion = text.IndexOf('?', StringComparison.Ordinal);
        Assert.Equal(text.IndexOf('?', firstQuestion + 1, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal(1, diagnostic.Span.Length);
        Assert.Equal(2, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration));
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("int32??? F() {}")]
    [InlineData("struct S { int32??? value; }")]
    [InlineData("void F() { int32??? value = Get(); }")]
    [InlineData("void F() { var value = new Box???(); }")]
    public void DiagnosesEveryExtraOptionalModifier(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.Equal(2, tree.Diagnostics.Length);
        Assert.All(tree.Diagnostics, diagnostic => {
            Assert.Equal("TRM1005", diagnostic.Code);
            Assert.Equal("?", tree.Source.GetSubText(diagnostic.Span));
        });
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("T?")]
    [InlineData("int32?")]
    [InlineData("List<int32?>?")]
    [InlineData("Map<T?, List<int32?>?>?")]
    [InlineData("Option<Option<int32>>")]
    public void AllowsOptionalModifiersAtSeparateNestingLevels(string type)
    {
        var tree = SyntaxTree.Parse("void F(" + type + " value) {}");
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("void F() { return 1 + 2 * 3; }", SyntaxKind.BinaryExpression, "+", "*")]
    [InlineData("void F() { return 1 * 2 + 3; }", SyntaxKind.BinaryExpression, "+", "*")]
    [InlineData("void F() { return a || b && c; }", SyntaxKind.BinaryExpression, "||", "&&")]
    public void PreservesOperatorPrecedence(string text, SyntaxKind kind, string outer, string inner)
    {
        var binaries = SyntaxTree.Parse(text).Root.DescendantNodes().Where(node => node.Kind == kind).ToArray();
        Assert.Equal(outer, Assert.IsType<SyntaxTokenElement>(binaries[0].Children[1]).Token.Text);
        Assert.Equal(inner, Assert.IsType<SyntaxTokenElement>(binaries[1].Children[1]).Token.Text);
    }

    [Fact]
    public void BinaryOperatorsAssociateToTheLeft()
    {
        var tree = SyntaxTree.Parse("int32 F() { return a - b - c; }");
        var outer = tree.Root.DescendantNodes().First(node => node.Kind == SyntaxKind.BinaryExpression);
        Assert.Equal(SyntaxKind.BinaryExpression, Assert.IsType<SyntaxNode>(outer.Children[0]).Kind);
        Assert.Equal(SyntaxKind.NameExpression, Assert.IsType<SyntaxNode>(outer.Children[2]).Kind);
    }

    [Theory]
    [InlineData("void F( { return ;")]
    [InlineData("void F() { var a = ; @@@ return 2 }")]
    [InlineData("class C { void F( {} } void G() { return; }")]
    [InlineData("void F() { if (true) return; }")]
    [InlineData("void F() { 1abc; F(,); }")]
    [InlineData("namespace A; namespace B; using C;")]
    [InlineData("} } }")]
    [InlineData("void F() { x = y = z; }")]
    [InlineData("void F() { var x = $\"unsupported\"; }")]
    [InlineData("void F() { require ; return; }")]
    [InlineData("void F() { require true }")]
    public void RecoversWithoutLosingText(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.True(tree.HasErrors);
        AssertLossless(tree);
        Assert.All(tree.Diagnostics, diagnostic => Assert.InRange(diagnostic.Span.End, 0, text.Length));
    }

    [Fact]
    public void ClassMembersAndFollowingDeclarationsRemainLossless()
    {
        var tree = SyntaxTree.Parse("class C { void F() {} } void G() { return; }");
        Assert.False(tree.HasErrors);
        AssertLossless(tree);
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ClassDeclaration);
        Assert.Equal(2, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration));
    }

    [Fact]
    public void MissingTokensAreExplicitAndDoNotInventText()
    {
        var tree = SyntaxTree.Parse("void F() { return 1 }");
        var missing = Assert.Single(tree.Root.DescendantTokens(), token => token.IsMissing);
        Assert.Equal(TokenKind.Semicolon, missing.Kind);
        Assert.Equal("", missing.Text);
        Assert.Equal(0, missing.Span.Length);
        AssertLossless(tree);
    }

    [Fact]
    public void UnsupportedDeclarationDoesNotHideFollowingFunction()
    {
        var tree = SyntaxTree.Parse("enum E { A, B } void Main() { return; }");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.FunctionDeclaration);
        AssertLossless(tree);
    }

    [Theory]
    [InlineData("void F(); public static void G() {}")]
    [InlineData("void F() { return; public static void G() {}")]
    [InlineData("void F() { static require true; public static void G() {}")]
    public void MissingBlockDoesNotConsumeFollowingDeclaration(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.True(tree.HasErrors);
        Assert.Equal(2, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration));
        AssertLossless(tree);
    }

    [Fact]
    public void BoundsRecursiveNestingButTraversesLongLeftAssociativeTrees()
    {
        var nested = "void F() { return " + new string('(', 2000) + "1" + new string(')', 2000) + "; }";
        var tree = SyntaxTree.Parse(nested);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Code == "TRM1004");
        AssertLossless(tree);

        var flat = "void F() { return " + string.Join("+", Enumerable.Repeat("1", 5000)) + "; }";
        var flatTree = SyntaxTree.Parse(flat);
        Assert.False(flatTree.HasErrors);
        AssertLossless(flatTree);
    }

    [Fact]
    public void CancellationIsNotReportedAsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => SyntaxTree.Parse("void F() {}", cancellationToken: cancellation.Token));
    }

    [Fact]
    public void DeterministicMalformedCorpusAlwaysProgresses()
    {
        var random = new Random(12345);
        string[] pieces = ["void", "return", "x", "{", "}", "(", ")", "[", "]", ";", "+", "=", ",", " ", "\r\n", "\uD800", "/*", "\"", "mut", "ref", "<", ">"];

        for (var iteration = 0; iteration < 200; iteration++)
        {
            var text = string.Concat(Enumerable.Range(0, 60).Select(_ => pieces[random.Next(pieces.Length)]));
            AssertLossless(SyntaxTree.Parse(text));
        }
    }

    private static void AssertLossless(SyntaxTree tree)
    {
        Assert.Equal(tree.Source.Text, tree.Root.ToFullString());
        Assert.Equal(tree.Tokens.ToArray(), [.. tree.Root.DescendantTokens().Where(token => !token.IsMissing)]);
        Assert.Equal(0, tree.Root.FullSpan.Start);
        Assert.Equal(tree.Source.Length, tree.Root.FullSpan.End);

        foreach (var node in tree.Root.DescendantNodes(includeSelf: true))
        {
            foreach (var child in node.Children)
            {
                Assert.True(node.FullSpan.Start <= child.FullSpan.Start);
                Assert.True(node.FullSpan.End >= child.FullSpan.End);
            }
        }
    }
}
