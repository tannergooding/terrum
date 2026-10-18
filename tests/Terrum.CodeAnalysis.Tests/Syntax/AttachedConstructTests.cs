// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class AttachedConstructTests
{
    private static readonly string[] Forms = [
        "int32 Main#() { return 42; }",
        "int32 @await#(int32 value) { return value; }",
        "int32 \U00010400#() { return 42; }",
        "struct Box { public Box#(int32 value) { } }",
        "struct @struct { public @struct#(int32 value) { } }",
        "struct Box { public static Box operator +#(Box left, Box right) { return left; } }",
        "struct Box { public static bool operator <#(Box left, Box right) { return true; } }",
        "[Policy#(Checked)] int32 Main() { return 42; }",
        "[@await#(Checked)] int32 Main() { return 42; }",
        "interface Box#<T> { }",
        "interface @interface#<this Self> { }",
        "int32 F#<T>() { return 42; }",
        "Box#<int32> F() { return value; }",
        "@struct#<int32> F() { return value; }",
        "Box.Inner#<Box<int32>> F() { return value; }",
        "void F(init ref#<int32> value) { }",
        "int32 Main() { return F#(42); }",
        "int32 Main() { return @await#(42); }",
        "int32 Main() { return value.Get#(42); }",
        "int32 Main() { return N.@await#(42); }",
        "int32 Main() { return (F)#(42); }",
        "int32 Main() { return F()#(42); }",
        "int32 Main() { return values#[0]; }",
        "int32 Main() { return (values)#[0]; }",
        "int32 Main() { return F()#[0]; }",
        "int32 Main() { return values[0]#[1]; }",
        "int32 Main() { return F#<int32>(42); }",
        "int32 Main() { return @await#<int32>(42); }",
        "int32 Main() { return F<int32>#(42); }",
        "int32 Main() { return F<Box<Box<int32>>>#(42); }",
        "int32 Main() { return new Box#(42); }",
        "int32 Main() { return new Box#<int32>(42); }",
    ];

    public static IEnumerable<object[]> Boundaries =>
        from form in Forms
        from gap in new[] { "", " ", "\n", "/* boundary */", "/// boundary\n" }
        select new object[] { form, gap };
    [TestCaseSource(nameof(Boundaries))]
    public void EverySupportedBoundaryUsesRawTokenExtents(string form, string gap)
    {
        var start = form.IndexOf('#');
        var source = form.Replace("#", gap, StringComparison.Ordinal);
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.That(lexed.Diagnostics, Is.Empty);
        foreach (var tree in new[] { SyntaxTree.Parse(source), SyntaxTree.Parse(lexed) })
        {
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            if (gap.Length == 0)
            {
                Assert.That(tree.Diagnostics, Is.Empty);
            }
            else
            {
                var diagnostic = global::System.Linq.Enumerable.Single(tree.Diagnostics);
                Assert.That(diagnostic.Code, Is.EqualTo("TRM1006"));
                Assert.That(diagnostic.Span, Is.EqualTo(new TextSpan(start + gap.Length, 1)));
            }
        }
    }
    [TestCase("int32 Main(\n) { return F(\n42\n); }")]
    [TestCase("interface Box<\nT\n> { }")]
    [TestCase("int32 F<\nT\n>(\nT value\n) { return value; }")]
    [TestCase("Box<\nint32\n> F() { return value; }")]
    [TestCase("int32 Main() { return F<\nint32\n>(\n42\n); }")]
    [TestCase("int32 Main() { return values[\n0\n]; }")]
    [TestCase("[Policy(\nChecked\n)] int32 Main() { return 42; }")]
    [TestCase("int32 Main() { if /* structural */ (true) { return ( /* grouping */ 42); } return 0; }")]
    [TestCase("int32 Main() { while \n(false) { } for /* structural */ (;;) { break; } return 42; }")]
    public void InteriorAndStructuralTriviaRemainPermissive(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.Diagnostics, Is.Empty);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("int32 Main() { return F /* boundary */ <int32>(42); }",true)]
    [TestCase("int32 Main() { return F<int32> /* boundary */ (42); }",true)]
    [TestCase("int32 Main() { return left < right; }",false)]
    [TestCase("int32 Main() { return left < right > value; }",false)]
    public void GenericShapeSelectionDoesNotDependOnAttachment(string source, bool generic)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.Root.DescendantNodes().Any(node => node.Kind == SyntaxKind.GenericNameExpression), Is.EqualTo(generic));
        Assert.That(tree.Diagnostics.Any(diagnostic => diagnostic.Code == "TRM1006"), Is.EqualTo(generic));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("int32 Main /* boundary */ (")]
    [TestCase("int32 Main() { return F /* boundary */ (")]
    [TestCase("int32 Main() { return values /* boundary */ [")]
    [TestCase("interface Box /* boundary */ <")]
    [TestCase("Box /* boundary */ <int32")]
    public void IncompleteSourceStillRoundTrips(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.Diagnostics, Is.Not.Empty);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,diagnostic => diagnostic.Code == "TRM1006"),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\n")]
    [TestCase("/* boundary */")]
    [TestCase("/// boundary\n")]
    public void HandAuthoredHistoricalProjectionCannotAuthorizeDetachedHeader(string gap)
    {
        var source = SourceText.From("int32 Main" + gap + "() { return 42; }");
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = 1;
        words[9] = 6;
        words[10] = 4;
        words[22] = 1;
        words[23] = 3;
        words[24] = 42;
        words[26] = source.ToString().IndexOf("42", StringComparison.Ordinal);
        words[27] = 2;
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        if (gap.Length == 0)
        {
            Assert.That(ScalarFrontendHost.ReadResult(source, bytes).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        }
    }
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\n")]
    [TestCase("/* boundary */")]
    [TestCase("/// boundary\n")]
    public void HandAuthoredConversionProjectionCannotAuthorizeDetachedConstructs(string gap)
    {
        foreach (var text in new[] {
            "public int32 @await" + gap + "() => int32(uint64(42));",
            "public int32 @await() => int32" + gap + "(uint64(42));",
            "public int32 @await() => int32(uint64" + gap + "(42));",
        })
        {
            var words = new int[ScalarFrontendHost.WordCount];
            words[0] = 87;
            words[1] = 1;
            words[2] = 1;
            words[5] = 1;
            words[9] = text.IndexOf("@await", StringComparison.Ordinal);
            words[10] = 6;
            words[37] = 3;
            words[38] = -1;
            words[39] = 2;
            words[40] = -1;
            words[41] = 1;
            var outer = text.IndexOf("int32", words[9], StringComparison.Ordinal);
            var inner = text.IndexOf("uint64", StringComparison.Ordinal);
            var literal = text.IndexOf("42", StringComparison.Ordinal);
            Node(0, 3, 4, literal, literal + 2, -1, 42);
            Node(1, 83, 4, inner, literal + 3, 0, 0);
            Node(2, 83, 1, outer, literal + 4, 1, 0);
            var bytes = new byte[words.Length * 4];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
            }
            if (gap.Length == 0)
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes).IsValid, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
            }

            void Node(int index, int operation, int type, int start, int end, int left, int low)
            {
                var offset = 42 + index * 8;
                words[offset] = operation;
                words[offset + 1] = type;
                words[offset + 2] = start;
                words[offset + 3] = end - start;
                words[offset + 4] = left;
                words[offset + 5] = -1;
                words[offset + 6] = low;
            }
        }
    }
}
