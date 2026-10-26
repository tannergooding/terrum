// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class RawInterpolatedTextSyntaxTests
{
    [Test]
    public void LexesAndParsesDollarCountedHolesAndLiteralBraceRemainders()
    {
        const string source = "void F(int32 value) { var text = $$\"\"\"A {{{value}}} B\"\"\"; }";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var tokens = tree.Tokens;
        Assert.That(tokens.Select(static token => token.Kind), Does.Contain(TokenKind.InterpolatedRawTextStart));
        Assert.That(tokens.Select(static token => token.Kind), Does.Contain(TokenKind.InterpolationHoleStart));
        Assert.That(tokens.Select(static token => token.Kind), Does.Contain(TokenKind.InterpolationHoleEnd));
        Assert.That(tokens.Select(static token => token.Kind), Does.Contain(TokenKind.InterpolatedRawTextEnd));
        Assert.That(tokens.Single(static token => token.Kind == TokenKind.InterpolatedRawTextStart).Text,
            Is.EqualTo("$$\"\"\""));
        Assert.That(tokens.Single(static token => token.Kind == TokenKind.InterpolationHoleStart).Text,
            Is.EqualTo("{{"));
        Assert.That(tokens.Single(static token => token.Kind == TokenKind.InterpolationHoleEnd).Text,
            Is.EqualTo("}}"));
        Assert.That(tokens.Count(static token => token.Kind == TokenKind.InterpolatedRawTextSegment), Is.EqualTo(2));
        Assert.That(tree.Root.DescendantNodes().Count(static node => node.Kind == SyntaxKind.InterpolationHole),
            Is.EqualTo(1));
    }

    [Test]
    public void ParsesConditionalExpressionsAlignmentAndStaticFormats()
    {
        const string source =
            "void F(bool flag, int32 left, int32 right, int32 width) { var text = $$\"\"\"{{flag ? left : right, width:X8}}\"\"\"; }";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.Root.DescendantNodes().Any(static node =>
            node.Kind == SyntaxKind.ConditionalExpression), Is.True);
        Assert.That(tree.Root.DescendantNodes().Any(static node =>
            node.Kind == SyntaxKind.InterpolationFormatSpecifier), Is.True);
    }

    [Test]
    public void ParsesNestedExpressionsAndQuotedFormats()
    {
        const string source =
            "void F(int32 value) { var text = $$\"\"\"{{Format((value + 1), value):\"X8\"}}\"\"\"; }";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.Root.DescendantNodes().Count(static node => node.Kind == SyntaxKind.InterpolationHole),
            Is.EqualTo(1));
        Assert.That(tree.Root.DescendantNodes().Any(static node =>
            node.Kind == SyntaxKind.InterpolationFormatSpecifier), Is.True);
    }

    [Test]
    public void ParsesRawAndParenthesizedFormatValues()
    {
        var sources = new[] {
            "void F(int32 value) { var text = $$\"\"\"{{value: \"\"\"X8\"\"\"}}\"\"\"; }",
            "void F(int32 value, int32 width) { var text = $$\"\"\"{{value, -width: (\"\"\"X8\"\"\")}}\"\"\"; }",
        };

        foreach (var source in sources)
        {
            var tree = SyntaxTree.Parse(source);

            Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            Assert.That(tree.Root.DescendantNodes().Count(static node =>
                node.Kind == SyntaxKind.InterpolationFormatSpecifier), Is.EqualTo(1));
        }
    }

    [Test]
    public void AppliesDollarCountToBraceRunsIndependentlyOfQuoteFenceLength()
    {
        var cases = new (string Interpolation, int HoleCount)[] {
            ("$\"\"\"{value}\"\"\"", 1),
            ("$$\"\"\"{literal}\"\"\"", 0),
            ("$$$\"\"\"{{{{value}}}}\"\"\"", 1),
            ("$$\"\"\"\"{{value}}\"\"\"\"", 1),
        };

        foreach (var (interpolation, expectedHoleCount) in cases)
        {
            var source = "void F(int32 value) { var text = " + interpolation + "; }";
            var tree = SyntaxTree.Parse(source);

            Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            Assert.That(tree.Root.DescendantNodes().Count(static node =>
                node.Kind == SyntaxKind.InterpolationHole), Is.EqualTo(expectedHoleCount), interpolation);
        }
    }

    [Test]
    public void LexesMultilineRawInterpolationAndKeepsFollowingSource()
    {
        const string source =
            "void F(int32 value) { var text = $$\"\"\"\n  first\n  {{value}}\n  last\n  \"\"\"; return; }";
        var result = Lexer.Lex(SourceText.From(source));
        var tree = SyntaxTree.Parse(new LexResult(result.Source, result.Tokens, result.Diagnostics));

        Assert.That(tree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.Tokens.Any(static token => token.Kind == TokenKind.ReturnKeyword), Is.True);
        Assert.That(tree.Tokens.Any(static token => token.Kind == TokenKind.InterpolatedRawTextEnd), Is.True);
    }

    [Test]
    public void AcceptsNewlineDirectivesAndNestedRawInterpolationHoles()
    {
        const string directedSource =
            "void F(int32 value) { var text = $$\"\"\"\\r\\n\n  first\n  {{value}}\n  last\n  \"\"\"; }";
        var directedTree = SyntaxTree.Parse(directedSource);
        Assert.That(directedTree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, directedTree.Diagnostics));
        Assert.That(directedTree.Root.ToFullString(), Is.EqualTo(directedSource));

        const string nestedSource =
            "void F(int32 value) { var text = $$\"\"\"outer {{$$\"\"\"inner {{value}}\"\"\"}} done\"\"\"; }";
        var nestedTree = SyntaxTree.Parse(nestedSource);
        Assert.That(nestedTree.Diagnostics, Is.Empty, string.Join(Environment.NewLine, nestedTree.Diagnostics));
        Assert.That(nestedTree.Root.ToFullString(), Is.EqualTo(nestedSource));
        Assert.That(nestedTree.Root.DescendantNodes().Count(static node =>
            node.Kind == SyntaxKind.InterpolatedRawTextExpression), Is.EqualTo(2));
        Assert.That(nestedTree.Root.DescendantNodes().Count(static node =>
            node.Kind == SyntaxKind.InterpolationHole), Is.EqualTo(2));
    }

    [Test]
    public void RecoversMalformedHeaderAtTheMatchingFence()
    {
        const string source = "void F(int32 value) { var text = $$\"\"\"\\t\ntext\n\"\"\"; return; }";
        var result = Lexer.Lex(SourceText.From(source));

        Assert.That(result.Diagnostics.Length, Is.EqualTo(1));
        Assert.That(result.Diagnostics[0].Code, Is.EqualTo("TRM0001"));
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.InterpolatedRawTextEnd), Is.True);
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.ReturnKeyword), Is.True);
    }

    [Test]
    public void RejectsBoundaryBraceRunsWithoutLosingTheClosingFence()
    {
        const string source = "void F(int32 value) { var text = $$\"\"\"{{value}}}}\"\"\"; return; }";
        var result = Lexer.Lex(SourceText.From(source));

        Assert.That(result.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM0001"), Is.True);
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.InterpolatedRawTextEnd), Is.True);
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.ReturnKeyword), Is.True);
    }

    [Test]
    public void RejectsOpeningBraceRunsAtTwiceTheDelimiterLength()
    {
        const string source = "void F(int32 value) { var text = $$\"\"\"{{{{value}}}}\"\"\"; return; }";
        var result = Lexer.Lex(SourceText.From(source));

        Assert.That(result.Diagnostics.Length, Is.EqualTo(1));
        Assert.That(result.Diagnostics[0].Code, Is.EqualTo("TRM0001"));
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.InterpolatedRawTextEnd), Is.True);
        Assert.That(result.Tokens.Any(static token => token.Kind == TokenKind.ReturnKeyword), Is.True);
    }
}
