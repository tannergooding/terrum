// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class ParserTests
{
    [Test]
    public void DiscardStatementRetainsItsDistinctShapeAndTrivia()
    {
        const string Source = "int32 Main() { _ /* destination */ = int32(42); return 42; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(SyntaxTree.LanguageVersion, Is.EqualTo("syntax-46"));
        var discard = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.DiscardStatement);
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual0 = discard.Children[0]; Assert.That(__nunitActual0, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual0!; }))().Token.Kind, Is.EqualTo(TokenKind.Underscore));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual1 = discard.Children[1]; Assert.That(__nunitActual1, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual1!; }))().Token.Kind, Is.EqualTo(TokenKind.Equals));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual2 = discard.Children[2]; Assert.That(__nunitActual2, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual2!; }))().Kind, Is.EqualTo(SyntaxKind.InvocationExpression));
        AssertLossless(tree);
    }

    [Test]
    public void ParsesOnlyNamespaceLevelTypeAliases()
    {
        const string Source = "public alias Count = int32;";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(SyntaxTree.LanguageVersion, Is.EqualTo("syntax-46"));
        var alias = global::System.Linq.Enumerable.Single(tree.Root.Children.OfType<SyntaxNode>(), static node => node.Kind == SyntaxKind.AliasDeclaration);
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual3 = alias.Children[2]; Assert.That(__nunitActual3, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual3!; }))().Token.Name, Is.EqualTo("Count"));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual4 = alias.Children[4]; Assert.That(__nunitActual4, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual4!; }))().Kind, Is.EqualTo(SyntaxKind.TypeName));
        AssertLossless(tree);

        var spacing = SyntaxTree.Parse("alias Count=int32;");
        Assert.That(global::System.Linq.Enumerable.Any(spacing.Diagnostics,static diagnostic => diagnostic.Code == "TRM1008"),Is.True);

        var nested = SyntaxTree.Parse("struct S { alias Count = int32; }");
        Assert.That(nested.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(nested.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.AliasDeclaration),Is.False);
        AssertLossless(nested);
    }

    [Test]
    public void ParsesCollectionSpreadElementsLosslessly()
    {
        const string Source = "int32 Main() { var items = [First(), ..Source(), Last()]; return 0; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var initializer = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(),
            static node => node.Kind == SyntaxKind.ArrayInitializerExpression);
        var elements = initializer.Children.OfType<SyntaxNode>().ToArray();
        Assert.That(elements.Select(static element => element.Kind),
            Is.EqualTo(new[] {
                SyntaxKind.InvocationExpression,
                SyntaxKind.CollectionSpreadElement,
                SyntaxKind.InvocationExpression,
            }));

        var spread = elements[1];
        Assert.That(spread.Children.OfType<SyntaxTokenElement>()
            .Select(static token => token.Token.Kind), Is.EqualTo(new[] { TokenKind.DotDot }));
        Assert.That(spread.Children.OfType<SyntaxNode>().Single().Kind,
            Is.EqualTo(SyntaxKind.InvocationExpression));
        AssertLossless(tree);

        var separated = SyntaxTree.Parse("int32 Main() { var items = [. .Source()]; return 0; }");
        Assert.That(separated.HasErrors, Is.True);
        AssertLossless(separated);
    }

    [Test]
    public void ParsesGenericNominalTypeParameterLists()
    {
        const string Source = """
            public struct Pair<TLeft, TRight> { TLeft Left; TRight Right; }
            public mut class Box<T> { T Value; }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind is SyntaxKind.StructDeclaration or SyntaxKind.ClassDeclaration)
            .ToArray();
        Assert.That(declarations, Has.Length.EqualTo(2));
        Assert.That(declarations.Select(static declaration => declaration.Kind),
            Is.EqualTo(new[] { SyntaxKind.StructDeclaration, SyntaxKind.ClassDeclaration }));
        Assert.That(declarations.Select(declaration =>
                declaration.Children.OfType<SyntaxNode>()
                    .Single(static node => node.Kind == SyntaxKind.TypeParameterList)
                    .Children.OfType<SyntaxTokenElement>()
                    .Where(static token => token.Token.Kind == TokenKind.Identifier)
                    .Select(static token => token.Token.Name)),
            Is.EqualTo(new[] { new[] { "TLeft", "TRight" }, new[] { "T" } }));
        AssertLossless(tree);
    }

    [Test]
    public void ParsesGenericNominalConformancesAndConstraintsLosslessly()
    {
        const string Source = """
            interface IValue<this T> where T : [Copy] {}
            struct Pair<T> where T : [Copy] { int32 Count; }
            class Box<T> : IValue<T> where T : [Copy] {}
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind is SyntaxKind.StructDeclaration or SyntaxKind.ClassDeclaration)
            .ToArray();
        Assert.That(declarations, Has.Length.EqualTo(2));
        Assert.That(declarations.Select(declaration =>
                declaration.Children.OfType<SyntaxNode>()
                    .Where(static node => node.Kind is SyntaxKind.TypeParameterList
                        or SyntaxKind.ConformanceList or SyntaxKind.ConstraintClause)
                    .Select(static node => node.Kind)
                    .ToArray()),
            Is.EqualTo(new[] {
                new[] { SyntaxKind.TypeParameterList, SyntaxKind.ConstraintClause },
                new[] { SyntaxKind.TypeParameterList, SyntaxKind.ConformanceList, SyntaxKind.ConstraintClause },
            }.ToArray()));
        AssertLossless(tree);
    }

    [Test]
    public void ParsesQualifiedNativeImportAttributesAndBodylessCallables()
    {
        const string Source = """
            [Terrum.Interop.NativeImport("windows-x64.system32.kernel32.GetCurrentThreadId")]
            extern uint32 CurrentThreadId();
            public static void Main() {}
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var declaration = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes()
            .Where(static node => node.Kind == SyntaxKind.FunctionDeclaration)
            .Where(static node => node.Children.OfType<SyntaxNode>()
                .Any(static child => child.Kind == SyntaxKind.ExternalBody)));
        Assert.That(global::System.Linq.Enumerable.Any(declaration.Children,static child => child is SyntaxNode {
            Kind: SyntaxKind.ExternalBody,
        }),Is.True);
        var attribute = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), static node => node.Kind == SyntaxKind.Attribute);
        Assert.That(global::System.Linq.Enumerable.Any(attribute.Children,static child => child is SyntaxTokenElement {
            Token.Kind: TokenKind.StringLiteral,
        }),Is.True);
        AssertLossless(tree);
    }
    [TestCase("mut class Leaf { public mut int32 Value; public Leaf(int32 value) { Value = value; } public mut void Set(int32 value) { Value = value; } }")]
    [TestCase("bool Create(out write ManagedNode node) { return ManagedNode.TryCreate(42, out node); }")]
    [TestCase("mut class Link { public mut write Link? Next; public Link() { Next = null; } }")]
    [TestCase("void Update(write ManagedNode node) { node.SetValue(42); }")]
    public void ParsesManagedWriteCapabilitiesWithoutLosingSource(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }

    [Test]
    public void ParsesSliceRangesAndEndRelativeUpperBoundsWithoutLosingTrivia()
    {
        const string Source = """
            int32 Read(OwnedInt32Buffer items) {
                var all = items[..];
                var prefix = items[..2];
                var suffix = items[1..];
                var tail = items[1..^0];
                var separated = items[1.. ^1];
                var fromEnd = items[^1..];
                return 0;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.DescendantNodes().Count(
            static node => node.Kind == SyntaxKind.SliceRangeExpression), Is.EqualTo(6));
        Assert.That(tree.Root.DescendantNodes().Count(
            static node => node.Kind == SyntaxKind.EndRelativeIndexBound), Is.EqualTo(2));
        AssertLossless(tree);

        var unattached = SyntaxTree.Parse("int32 Read(OwnedInt32Buffer items) { var tail = items[..^ 1]; return 0; }");
        Assert.That(global::System.Linq.Enumerable.Any(unattached.Diagnostics,static diagnostic => diagnostic.Code == "TRM1009"),Is.True);
        AssertLossless(unattached);

        var unattachedIndex = SyntaxTree.Parse("int32 Read(OwnedInt32Buffer items) { return items[^ 1]; }");
        Assert.That(global::System.Linq.Enumerable.Any(unattachedIndex.Diagnostics,static diagnostic => diagnostic.Code == "TRM1009"),Is.True);
        AssertLossless(unattachedIndex);
    }
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    [TestCase("char16")]
    public void TypeCallsPreserveTypeSpellingAndBindMoreTightlyThanAddition(string type)
    {
        var tree = SyntaxTree.Parse("void F() { return " + type + "(/* conversion */ value) + 1; }");
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var cast = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.InvocationExpression);
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual6 = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual5 = cast.Children[0]; Assert.That(__nunitActual5, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual5!; }))().Children[0]; Assert.That(__nunitActual6, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual6!; }))().Token.Text, Is.EqualTo(type));
        var arguments = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual7 = cast.Children[1]; Assert.That(__nunitActual7, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual7!; }))();
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual9 = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual8 = arguments.Children[1]; Assert.That(__nunitActual8, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual8!; }))().Children[0]; Assert.That(__nunitActual9, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual9!; }))().Kind, Is.EqualTo(SyntaxKind.NameExpression));
        var addition = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.BinaryExpression);
        Assert.That(addition.Children[0], Is.SameAs(cast));
        AssertLossless(tree);
    }
    [TestCase("void F() { require /* \u03B1 */ ready; }")]
    [TestCase("void F() { require TryRead(out var value) && value > 0; }")]
    public void ParsesRequirementsWithoutLosingTriviaOrSourceShape(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var requirement = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.RequireStatement);
        Assert.That(requirement.Children.Length, Is.EqualTo(3));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual10 = requirement.Children[0]; Assert.That(__nunitActual10, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual10!; }))().Token.Kind, Is.EqualTo(TokenKind.RequireKeyword));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual11 = requirement.Children[2]; Assert.That(__nunitActual11, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual11!; }))().Token.Kind, Is.EqualTo(TokenKind.Semicolon));
        AssertLossless(tree);
    }

    [Test]
    public void TryFinallyRetainsBothBlocksAndKeywordTrivia()
    {
        const string Source = "void F() { /* before */ try /* body */ { return; } /* keyword */ finally /* cleanup */ { ; } }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var region = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryFinallyStatement);
        Assert.That(region.Children.Length, Is.EqualTo(4));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual12 = region.Children[0]; Assert.That(__nunitActual12, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual12!; }))().Token.Kind, Is.EqualTo(TokenKind.TryKeyword));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual13 = region.Children[1]; Assert.That(__nunitActual13, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual13!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        var keyword = ((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual14 = region.Children[2]; Assert.That(__nunitActual14, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual14!; }))().Token;
        Assert.That(keyword.Kind, Is.EqualTo(TokenKind.FinallyKeyword));
        Assert.That(global::System.Linq.Enumerable.Any(keyword.LeadingTrivia,trivia => trivia.Text == "/* keyword */"),Is.True);
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual15 = region.Children[3]; Assert.That(__nunitActual15, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual15!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        Assert.That(region.Span.Start, Is.EqualTo(Source.IndexOf("try", StringComparison.Ordinal)));
        Assert.That(region.Span.End, Is.EqualTo(Source.LastIndexOf("} }", StringComparison.Ordinal) + 1));
        AssertLossless(tree);
    }
    [TestCase("void F() { try { return; } return; }",TokenKind.FinallyKeyword,2)]
    [TestCase("void F() { try { return; } finally return; }",TokenKind.OpenBrace,2)]
    [TestCase("void F() { try finally { } return; }",TokenKind.OpenBrace,1)]
    public void MalformedTryFinallyPreservesMissingSyntaxAndFollowingStatements(string source, TokenKind missingKind, int returns)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        var region = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryFinallyStatement);
        Assert.That(region.Children.Length, Is.EqualTo(4));
        Assert.That(global::System.Linq.Enumerable.Any(region.DescendantTokens(),token => token.Kind == missingKind && token.IsMissing
            && token.Text.Length == 0 && token.Span.Length == 0),Is.True);
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ReturnStatement), Is.EqualTo(returns));
        AssertLossless(tree);
    }

    [Test]
    public void UnsafeRecoveryRetainsBothRegionsOptionalCleanupAndKeywordTrivia()
    {
        const string Source = "void F() { unsafe /* scope */ { try /* body */ { require false; } /* recovery */ catch /* block */ { ; } /* last */ finally { ; } } }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var unsafeBlock = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.UnsafeStatement);
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual16 = unsafeBlock.Children[0]; Assert.That(__nunitActual16, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual16!; }))().Token.Kind, Is.EqualTo(TokenKind.UnsafeKeyword));
        var scope = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual17 = unsafeBlock.Children[1]; Assert.That(__nunitActual17, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual17!; }))();
        var open = ((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual18 = scope.Children[0]; Assert.That(__nunitActual18, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual18!; }))().Token;
        Assert.That(global::System.Linq.Enumerable.Any(open.LeadingTrivia,trivia => trivia.Text == "/* scope */"),Is.True);
        var region = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryCatchStatement);
        Assert.That(region.Children.Length, Is.EqualTo(6));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual19 = region.Children[1]; Assert.That(__nunitActual19, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual19!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual20 = region.Children[2]; Assert.That(__nunitActual20, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual20!; }))().Token.LeadingTrivia,trivia => trivia.Text == "/* recovery */"),Is.True);
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual21 = region.Children[3]; Assert.That(__nunitActual21, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual21!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual22 = region.Children[4]; Assert.That(__nunitActual22, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual22!; }))().Token.Kind, Is.EqualTo(TokenKind.FinallyKeyword));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual23 = region.Children[5]; Assert.That(__nunitActual23, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual23!; }))().Kind, Is.EqualTo(SyntaxKind.Block));
        AssertLossless(tree);
    }
    [TestCase("void F() { unsafe { try { } catch return; } return; }",TokenKind.OpenBrace)]
    [TestCase("void F() { unsafe { try { } catch { } finally return; } return; }",TokenKind.OpenBrace)]
    public void MalformedRecoveryPreservesMissingBlocksAndFollowingStatements(string source, TokenKind missingKind)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        var region = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.TryCatchStatement);
        Assert.That(global::System.Linq.Enumerable.Any(region.DescendantTokens(),token => token.Kind == missingKind && token.IsMissing),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.ReturnStatement),Is.True);
        AssertLossless(tree);
    }
    [TestCase("")]
    [TestCase("// only trivia\r\n")]
    [TestCase("using Terrum; public static void Main() { Terminal.WriteLine(\"Hello, world!\"); }")]
    [TestCase("namespace Demo; int32 Add(int32 left, int32 right) { return left + right; }")]
    [TestCase("void F(Span<TextSpan> args) { mut int32 i = 0; while (i < args.Length) { if (i == 2) { return; } else { i = i + 1; } } }")]
    [TestCase("public struct Point { public int32 X; public int32 Y; public Point(int32 x, int32 y) { X = x; Y = y; } }")]
    [TestCase("void F(ref int32 a, mut ref int32 b, out int32 c) { var ok = T.TryParse(a, out var v); G(mut ref b, name: v); return (); }")]
    [TestCase("void F() { Namespace.List<Span<int32?>?>? values = Get(); var x = a[0].F(); }")]
    [TestCase("void F() { var point = new Point(3, 4); }")]
    public void ParsesSupportedSyntax(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }
    [TestCase("int32??")]
    [TestCase("T??")]
    [TestCase("T? /* gap */ ?")]
    [TestCase("int32?\r\n?")]
    [TestCase("List<int32??>?")]
    [TestCase("List<int32>??")]
    public void RejectsRepeatedOptionalModifiersWithoutLosingText(string type)
    {
        var text = "void F(" + type + " value) {} void G() {}";
        var tree = SyntaxTree.Parse(text);
        var diagnostic = global::System.Linq.Enumerable.Single(tree.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM1005"));
        var firstQuestion = text.IndexOf('?', StringComparison.Ordinal);
        Assert.That(diagnostic.Span.Start, Is.EqualTo(text.IndexOf('?', firstQuestion + 1, StringComparison.Ordinal)));
        Assert.That(diagnostic.Span.Length, Is.EqualTo(1));
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration), Is.EqualTo(2));
        AssertLossless(tree);
    }
    [TestCase("int32??? F() {}")]
    [TestCase("struct S { int32??? value; }")]
    [TestCase("void F() { int32??? value = Get(); }")]
    [TestCase("void F() { var value = new Box???(); }")]
    public void DiagnosesEveryExtraOptionalModifier(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.Diagnostics.Length, Is.EqualTo(2));
        {var __nunitItems0 = tree.Diagnostics;        Assert.Multiple(() => {
            foreach (var __nunitItem24 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem24.Code, Is.EqualTo("TRM1005"));
                    Assert.That(tree.Source.GetSubText(__nunitItem24.Span), Is.EqualTo("?"));
                }
            }
        });}
        AssertLossless(tree);
    }
    [TestCase("T?")]
    [TestCase("int32?")]
    [TestCase("List<int32?>?")]
    [TestCase("Map<T?, List<int32?>?>?")]
    [TestCase("Option<Option<int32>>")]
    [TestCase("Option<Option<Option<int32>>>")]
    [TestCase("Option<Option<Option<Option<int32>>>>")]
    [TestCase("Option<Option<int32 /* close */ >>")]
    public void AllowsOptionalModifiersAtSeparateNestingLevels(string type)
    {
        var tree = SyntaxTree.Parse("void F(" + type + " value) {}");
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        AssertLossless(tree);
    }
    [TestCase("void F() { return 1 + 2 * 3; }",SyntaxKind.BinaryExpression,"+","*")]
    [TestCase("void F() { return 1 * 2 + 3; }",SyntaxKind.BinaryExpression,"+","*")]
    [TestCase("void F() { return a || b && c; }",SyntaxKind.BinaryExpression,"||","&&")]
    public void PreservesOperatorPrecedence(string text, SyntaxKind kind, string outer, string inner)
    {
        var binaries = SyntaxTree.Parse(text).Root.DescendantNodes().Where(node => node.Kind == kind).ToArray();
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual25 = binaries[0].Children[1]; Assert.That(__nunitActual25, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual25!; }))().Token.Text, Is.EqualTo(outer));
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual26 = binaries[1].Children[1]; Assert.That(__nunitActual26, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual26!; }))().Token.Text, Is.EqualTo(inner));
    }

    [Test]
    public void BinaryOperatorsAssociateToTheLeft()
    {
        var tree = SyntaxTree.Parse("int32 F() { return a - b - c; }");
        var outer = tree.Root.DescendantNodes().First(node => node.Kind == SyntaxKind.BinaryExpression);
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual27 = outer.Children[0]; Assert.That(__nunitActual27, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual27!; }))().Kind, Is.EqualTo(SyntaxKind.BinaryExpression));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual28 = outer.Children[2]; Assert.That(__nunitActual28, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual28!; }))().Kind, Is.EqualTo(SyntaxKind.NameExpression));
    }

    [Test]
    public void ParsesNestedGenericCallsWithTheirExistingSyntaxShape()
    {
        const string Source = "void F() { Outer<Inner<Box<int32>>>(Leaf<T>(value)); }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));

        var genericNames = tree.Root.DescendantNodes()
            .Where(static node => node.Kind == SyntaxKind.GenericNameExpression).ToArray();
        Assert.That(genericNames.Length, Is.EqualTo(2));
        Assert.That(tree.Root.DescendantNodes().Count(
            static node => node.Kind == SyntaxKind.InvocationExpression), Is.EqualTo(2));

        var outer = global::System.Linq.Enumerable.Single(genericNames, static node =>
            ((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual29 = node.Children[0]; Assert.That(__nunitActual29, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual29!; }))().Token.Text == "Outer");
        var outerArguments = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual30 = outer.Children[1]; Assert.That(__nunitActual30, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual30!; }))();
        var inner = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual31 = outerArguments.Children[1]; Assert.That(__nunitActual31, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual31!; }))();
        var box = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual32 = inner.Children[2]; Assert.That(__nunitActual32, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual32!; }))();
        var primitive = ((global::System.Func<SyntaxNode>)(() => { var __nunitActual33 = box.Children[2]; Assert.That(__nunitActual33, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual33!; }))();
        Assert.That(inner.Kind, Is.EqualTo(SyntaxKind.TypeName));
        Assert.That(box.Kind, Is.EqualTo(SyntaxKind.TypeName));
        Assert.That(primitive.Kind, Is.EqualTo(SyntaxKind.TypeName));
        var outerInvocation = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.InvocationExpression && ReferenceEquals(node.Children[0], outer));
        Assert.That(outerInvocation.Children[0], Is.SameAs(outer));
        Assert.That(outer.Span.Start, Is.EqualTo(Source.IndexOf("Outer", StringComparison.Ordinal)));
        Assert.That(outer.Span.End, Is.EqualTo(Source.IndexOf(">>>", StringComparison.Ordinal) + 3));
        AssertLossless(tree);
    }
    [TestCase("void F() { return F /* gap */ <int32>(value); }","<")]
    [TestCase("void F() { return F<int32> /* gap */ (value); }","(")]
    public void GenericCallsRemainSelectedWhenTheirDelimitersFailAttachment(string source, string unattached)
    {
        var tree = SyntaxTree.Parse(source);
        var diagnostic = global::System.Linq.Enumerable.Single(tree.Diagnostics);
        Assert.That(diagnostic.Code, Is.EqualTo("TRM1006"));
        Assert.That(diagnostic.Span.Start, Is.EqualTo(source.LastIndexOf(unattached, StringComparison.Ordinal)));
        Assert.That(diagnostic.Span.Length, Is.EqualTo(1));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.GenericNameExpression),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.InvocationExpression),Is.True);
        AssertLossless(tree);
    }

    [Test]
    public void MalformedGenericCandidateRecoversWithoutSelectingAGenericCall()
    {
        const string Source = "void F() { Outer<,>(value); return; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.GenericNameExpression),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.BinaryExpression),Is.True);
        Assert.That(global::System.Linq.Enumerable.Count(tree.Root.DescendantNodes(),static node => node.Kind == SyntaxKind.ReturnStatement),Is.EqualTo(1));
        {var __nunitItems1 = tree.Diagnostics;        Assert.Multiple(() => { foreach (var __nunitItem34 in __nunitItems1) { Assert.That(__nunitItem34.Span.End, Is.InRange(0, Source.Length)); } });}
        AssertLossless(tree);
    }
    [TestCase("void F( { return ;")]
    [TestCase("void F() { var a = ; @@@ return 2 }")]
    [TestCase("class C { void F( {} } void G() { return; }")]
    [TestCase("void F() { if (true) return; }")]
    [TestCase("void F() { 1abc; F(,); }")]
    [TestCase("namespace A; namespace B; using C;")]
    [TestCase("} } }")]
    [TestCase("void F() { x = y = z; }")]
    [TestCase("void F() { var x = $\"unsupported\"; }")]
    [TestCase("void F() { require ; return; }")]
    [TestCase("void F() { require true }")]
    public void RecoversWithoutLosingText(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.True);
        AssertLossless(tree);
        {var __nunitItems2 = tree.Diagnostics;        Assert.Multiple(() => { foreach (var __nunitItem35 in __nunitItems2) { Assert.That(__nunitItem35.Span.End, Is.InRange(0, text.Length)); } });}
    }

    [Test]
    public void ClassMembersAndFollowingDeclarationsRemainLossless()
    {
        var tree = SyntaxTree.Parse("class C { void F() {} } void G() { return; }");
        Assert.That(tree.HasErrors, Is.False);
        AssertLossless(tree);
        _ = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ClassDeclaration);
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration), Is.EqualTo(2));
    }

    [Test]
    public void MissingTokensAreExplicitAndDoNotInventText()
    {
        var tree = SyntaxTree.Parse("void F() { return 1 }");
        var missing = global::System.Linq.Enumerable.Single(tree.Root.DescendantTokens(), token => token.IsMissing);
        Assert.That(missing.Kind, Is.EqualTo(TokenKind.Semicolon));
        Assert.That(missing.Text, Is.EqualTo(""));
        Assert.That(missing.Span.Length, Is.EqualTo(0));
        AssertLossless(tree);
    }

    [Test]
    public void UnsupportedDeclarationDoesNotHideFollowingFunction()
    {
        var tree = SyntaxTree.Parse("enum E { A, B } void Main() { return; }");
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.FunctionDeclaration),Is.True);
        AssertLossless(tree);
    }
    [TestCase("void F() { return; public static void G() {}")]
    [TestCase("void F() { static require true; public static void G() {}")]
    public void MissingBlockDoesNotConsumeFollowingDeclaration(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration), Is.EqualTo(2));
        AssertLossless(tree);
    }

    [Test]
    public void BodylessCallableDoesNotConsumeFollowingDeclaration()
    {
        var tree = SyntaxTree.Parse("void F(); public static void G() {}");
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.FunctionDeclaration), Is.EqualTo(2));
        Assert.That(global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ExternalBody).Kind, Is.EqualTo(SyntaxKind.ExternalBody));
        AssertLossless(tree);
    }

    [Test]
    public void BoundsRecursiveNestingButTraversesLongLeftAssociativeTrees()
    {
        var nested = "void F() { return " + new string('(', 2000) + "1" + new string(')', 2000) + "; }";
        var tree = SyntaxTree.Parse(nested);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,diagnostic => diagnostic.Code == "TRM1004"),Is.True);
        AssertLossless(tree);

        var flat = "void F() { return " + string.Join(" + ", Enumerable.Repeat("1", 5000)) + "; }";
        var flatTree = SyntaxTree.Parse(flat);
        Assert.That(flatTree.HasErrors, Is.False);
        AssertLossless(flatTree);
    }

    [Test]
    public void CancellationIsNotReportedAsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => SyntaxTree.Parse("void F() {}", cancellationToken: cancellation.Token))!;
    }

    [Test]
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
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(tree.Source.Text));
        var parsed = tree.Root.DescendantTokens().Where(token => !token.IsMissing).ToArray();
        var at = 0;
        foreach (var lexical in tree.Tokens)
        {
            if (lexical.Kind is TokenKind.ShiftRight or TokenKind.ShiftRightUnsigned
                && parsed[at].Kind == TokenKind.Greater)
            {
                for (var part = 0; part < lexical.Text.Length; part++)
                {
                    var token = parsed[at++];
                    Assert.That(token.Kind, Is.EqualTo(TokenKind.Greater));
                    Assert.That(token.Text, Is.EqualTo(">"));
                    Assert.That(token.Span, Is.EqualTo(new TextSpan(lexical.Span.Start + part, 1)));
                    Assert.That(token.LeadingTrivia, Is.EqualTo(part == 0 ? lexical.LeadingTrivia : []));
                }
            }
            else
            {
                Assert.That(parsed[at++], Is.SameAs(lexical));
            }
        }
        Assert.That(at, Is.EqualTo(parsed.Length));
        Assert.That(tree.Root.FullSpan.Start, Is.EqualTo(0));
        Assert.That(tree.Root.FullSpan.End, Is.EqualTo(tree.Source.Length));

        foreach (var node in tree.Root.DescendantNodes(includeSelf: true))
        {
            foreach (var child in node.Children)
            {
                Assert.That(node.FullSpan.Start <= child.FullSpan.Start, Is.True);
                Assert.That(node.FullSpan.End >= child.FullSpan.End, Is.True);
            }
        }
    }
}
