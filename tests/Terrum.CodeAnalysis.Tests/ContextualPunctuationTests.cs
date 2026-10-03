// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

public sealed class ContextualPunctuationTests
{
    private static readonly string[] Gaps = ["", " ", "\n", "\r\n", "/* gap */", "//** gap **//", "// gap\n", "/// gap\n"];

    public static IEnumerable<object[]> Boundaries
    {
        get
        {
            foreach (var (form, punctuation) in new[] {
                ("int32 Main() { return @await#left : right; }", "?"),
                ("int32 Main() { return (flag)#(left) : right; }", "?"),
                ("int32 Main() { return Get()[0]#left : right; }", "?"),
                ("int32 Main() { return F<Box<Box<int32>>>()#left : right; }", "?"),
                ("int32 Main() { return flag ? @await#\U00010400; }", ":"),
                ("int32 Main() { return flag ? Get()[0]#(right); }", ":"),
                ("int32 Main() { return flag ? F<Box<Box<int32>>>()#right; }", ":"),
                ("struct @await#I<@await> { }", ":"),
                ("T F<T>(T value) where T#I<T> { return value; }", ":"),
            })
            {
                foreach (var gap in Gaps.Skip(1))
                {
                    yield return [form, punctuation, gap, gap, ""];
                    yield return [form, punctuation, "", gap, "TRM1008"];
                    yield return [form, punctuation, gap, "", "TRM1008"];
                }
                yield return [form, punctuation, "", "", "TRM1008"];
            }
            foreach (var (form, loopLabel) in new[] {
                ("int32 Main() { return F(@await#42); }", false),
                ("int32 Main() { return F(\U00010400#flag ? left : right); }", false),
                ("int32 Main() { @await#while (false) { } return 42; }", true),
                ("int32 Main() { \U00010400#for (;;) { break \U00010400; } return 42; }", true),
                ("int32 Main() { outer#do { break outer; } while (true); return 42; }", true),
            })
            {
                foreach (var gap in Gaps)
                {
                    var lineBreakAfterColon = gap.Contains('\r') || gap.Contains('\n');
                    var afterColonCode = loopLabel && lineBreakAfterColon ? "TRM1012" : "";
                    yield return [form, ":", "", gap, afterColonCode];
                    if (gap.Length != 0)
                    {
                        yield return [form, ":", gap, "", "TRM1010"];
                        var bothGapsCode = loopLabel && lineBreakAfterColon
                            ? "TRM1010|TRM1012" : "TRM1010";
                        yield return [form, ":", gap, gap, bothGapsCode];
                    }
                }
            }
            foreach (var form in new[] {
                "@await# F() { return value; }",
                "void F(N.\U00010400# value) { }",
                "struct S { public write Node# Next; }",
                "struct S { public Node# Value => value; }",
                "void F() { Node# value = null; }",
                "void F(init ref<Node#> value) { }",
                "Box<Box<Node>># F() { return value; }",
                "Box<\nBox<Node>\n># F() { return value; }",
                "void F() { Get<Box<Box<Node>>#>(); }",
            })
            {
                foreach (var gap in Gaps)
                {
                    yield return [form, "?", gap, "", gap.Length == 0 ? "" : "TRM1011"];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void RoleSelectedBeforeBindingUsesExactRawTokenBoundaries(
        string form, string punctuation, string left, string right, string code)
    {
        var source = form.Replace("#", left + punctuation + right, StringComparison.Ordinal);
        var span = new TextSpan(form.IndexOf('#') + left.Length, punctuation.Length);
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.Empty(lexed.Diagnostics);
        foreach (var tree in new[] { SyntaxTree.Parse(source), SyntaxTree.Parse(lexed) })
        {
            Assert.Equal(source, tree.Root.ToFullString());
            if (code.Length == 0)
            {
                Assert.Empty(tree.Diagnostics);
            }
            else
            {
                var codes = code.Split('|');
                Assert.Equal(codes, tree.Diagnostics.Select(static diagnostic => diagnostic.Code));
                Assert.All(tree.Diagnostics, diagnostic => Assert.Equal(span, diagnostic.Span));
            }
        }
    }

    [Theory]
    [InlineData("Node?? F() { return null; }")]
    [InlineData("Node? ? F() { return null; }")]
    [InlineData("Node?/* gap */? F() { return null; }")]
    [InlineData("Box<Box<Node>>? ? F() { return null; }")]
    public void RepeatedOptionalModifiersStillRejectWithoutChangingRoles(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Code == "TRM1005");
        Assert.Equal(source, tree.Root.ToFullString());
        Assert.DoesNotContain(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ConditionalExpression);
    }

    [Theory]
    [InlineData("void F() { Get<Box<Node /* gap */ ?>>(); }")]
    [InlineData("void F() { Get<Box<Box<Node>> /* gap */ ?>(); }")]
    public void OptionalGenericArgumentsDoNotFallBackAfterAttachmentFailure(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Equal("TRM1011", Assert.Single(tree.Diagnostics).Code);
        Assert.Contains(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.GenericNameExpression);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("int32 Main() { return flag?")]
    [InlineData("int32 Main() { return flag ? left:")]
    [InlineData("struct S:")]
    [InlineData("T F<T>() where T:")]
    [InlineData("int32 Main() { F(name :")]
    [InlineData("Node ?")]
    public void IncompletePunctuationRetainsEverySourceUnit(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("interface I : Other { }")]
    [InlineData("enum E : int32 { A = 1 }")]
    [InlineData("[param(value): Policy] void F(int32 value) { }")]
    [InlineData("void F() { var pair = (name: 1, other: 2); }")]
    public void UnsupportedColonRolesDoNotBecomeImplemented(string source) =>
        Assert.True(SyntaxTree.Parse(source).HasErrors);

    [Fact]
    public void StringInteriorsRemainOutsidePunctuationRoles()
    {
        const string source = "void F() { var text = \"flag?left:right:X8\"; }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
        Assert.Contains(tree.Tokens, token => token.Text == "\"flag?left:right:X8\"");
    }

    [Theory]
    [InlineData("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await:42); }")]
    [InlineData("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await:/* gap */42); }")]
    [InlineData("int32 Take(int32 \U00010400) => \U00010400; int32 Main() { return Take(\U00010400:42); }")]
    public void NamedArgumentsBindByNormalizedParameterName(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(source, tree.Root.ToFullString());
        var argument = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.Argument);
        Assert.Equal(TokenKind.Colon, Assert.IsType<SyntaxTokenElement>(argument.Children[1]).Token.Kind);
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(tree, (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NamedCallsEvaluateInSourceOrderAndAllowInPositionContinuation(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Record(int32 value) {
                Host.Trace[0] = Host.Trace[0] * 10 + value;
                return value;
            }
            int32 Combine(int32 first, int32 second, int32 third) {
                return first * 100 + second * 10 + third;
            }
            int32 Main() {
                int32 reordered = Combine(third: Record(3), first: Record(1), second: Record(2));
                if (reordered != 123 || Host.Trace[0] != 312) { return 1; }
                Host.Trace[0] = 0;
                int32 continued = Combine(first: Record(1), Record(2), third: Record(3));
                if (continued != 123 || Host.Trace[0] != 123) { return 2; }
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)], (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NamedInstanceArgumentsKeepTheReceiverInItsFormalSlot(NativeRuntimePolicy policy)
    {
        const string source = """
            struct Accumulator {
                public int32 Value;
                public Accumulator(int32 first, int32 second) { Value = first * 10 + second; }
                public int32 Combine(int32 first, int32 second) { return Value + first * 10 + second; }
            }
            int32 Main() {
                Accumulator value = new Accumulator(second: 2, first: 4);
                return value.Combine(second: 0, first: 0);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
    }

    [Theory]
    [InlineData("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(unknown: 1, second: 2); }")]
    [InlineData("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(first: 1, first: 2); }")]
    [InlineData("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(second: 2, 1); }")]
    [InlineData("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(first: 1); }")]
    [InlineData("bool Fill(out int32 value, int32 other) { value = other; return true; } int32 Main() { Fill(other: 1, value: out var result); return result; }")]
    public void InvalidNamedArgumentBindingsAreRejected(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Theory]
    [InlineData("int32 Main() { outer:while (true) { break outer; } return 42; }")]
    [InlineData("int32 Main() { @for:for (;;) { break @for; } return 42; }")]
    [InlineData("int32 Main() { \U00010400:do { break \U00010400; } while (true); return 42; }")]
    public void AttachedLabelsKeepUnderlyingBindingAndTransfers(string source)
    {
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerPreservesAllCurrentPunctuationRolesAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await File.ReadAllTextAsync(Path.Combine(root, "self-host", "Scanner.terrum"));
        var chunks = new List<string>();
        var chunk = new StringBuilder();
        foreach (var row in Boundaries)
        {
            var text = ((string)row[0]).Replace("#", (string)row[2] + row[1] + row[3], StringComparison.Ordinal) + "\n";
            if (chunk.Length + text.Length > ScannerHost.MaximumSourceLength)
            {
                chunks.Add(chunk.ToString());
                chunk.Clear();
            }
            chunk.Append(text);
        }
        chunks.Add(chunk.ToString());
        var failures = 0;
        foreach (var text in chunks)
        {
            var source = SourceText.From(text);
            var buffers = ScannerHost.CreateBuffers(source);
            var compilation = Compilation.Create(SyntaxTree.Parse(scanner + "int32 Main() { return Scan(); }"),
                buffers, (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var native = NativeJit.ExecuteWithData(Assert.IsType<BoundProgram>(compilation.Program), policy);
            Assert.Equal(Lexer.Lex(source).Tokens.Select(token => new ScannerToken(token.Kind, token.Span)),
                ScannerHost.ReadTokens(source, native.ExitCode, Buffer(3)));
            var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
            var current = SyntaxTree.Parse(adapted);
            Assert.Equal(text, current.Root.ToFullString());
            Assert.Equal(SyntaxTree.Parse(source).Diagnostics.ToArray(), current.Diagnostics.ToArray());
            failures += current.Diagnostics.Count(diagnostic =>
                diagnostic.Code is "TRM1008" or "TRM1010" or "TRM1011" or "TRM1012");

            ReadOnlySpan<byte> Buffer(int index) =>
                native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
        }
        var expectedFailures = Boundaries.Sum(row =>
            ((string)row[4]).Split('|', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(expectedFailures, failures);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(51)]
    public void IndependentConditionalBodyLayoutsRequireCurrentSpacing(int version)
    {
        foreach (var gap in Gaps.Skip(1))
        {
            foreach (var missing in new[] { -1, 0, 1, 2, 3 })
            {
                var spaces = Enumerable.Repeat(gap, 4).ToArray();
                if (missing >= 0)
                {
                    spaces[missing] = "";
                }
                var expression = "b" + spaces[0] + "?" + spaces[1] + "1" + spaces[2] + ":" + spaces[3] + "2";
                var text = version == 24
                    ? "public int32 Value(bool b) => " + expression + ";"
                    : "public int32 Value(bool b) { do { return " + expression + "; } while (false); }";
                var words = new int[ScalarFrontendHost.WordCount];
                words[0] = 85; words[1] = 1; words[2] = 1; words[5] = 1; words[6] = 1; words[7] = 5;
                words[9] = text.IndexOf("Value", StringComparison.Ordinal); words[10] = 5;
                words[11] = text.IndexOf("b)", StringComparison.Ordinal); words[12] = 1;
                words[37] = version == 24 ? 4 : 5;
                words[38] = -1; words[39] = version == 24 ? 3 : -1; words[40] = -1;
                words[41] = version == 24 ? 1 : 0;
                var start = text.IndexOf(expression, StringComparison.Ordinal);
                var one = start + 1 + spaces[0].Length + 1 + spaces[1].Length;
                var two = one + 1 + spaces[2].Length + 1 + spaces[3].Length;
                Node(0, 0, 5, start, start + 1, 1, -1, 0);
                Node(1, 3, 1, one, one + 1, -1, -1, 1);
                Node(2, 3, 1, two, two + 1, -1, -1, 2);
                Node(3, 84, 1, start, two + 1, 0, 1, 2);
                if (version == 51)
                {
                    var predicate = text.IndexOf("false", StringComparison.Ordinal);
                    Node(4, 21, 5, predicate, predicate + 5, -1, -1, 0);
                    words[298] = 2;
                    var loop = text.IndexOf("do {", StringComparison.Ordinal);
                    var result = text.IndexOf("return", StringComparison.Ordinal);
                    int[] statements = [11, 2, 4, loop, text.IndexOf(");", StringComparison.Ordinal) + 2 - loop,
                        8, 0, 3, result, two + 2 - result];
                    statements.CopyTo(words, 332);
                }
                var bytes = new byte[words.Length * sizeof(int)];
                for (var index = 0; index < words.Length; index++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
                }
                if (missing == -1)
                {
                    Assert.True(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes).IsValid);
                }
                else
                {
                    Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
                }

                void Node(int index, int operation, int type, int begin, int end, int left, int right, int low)
                {
                    int[] node = [operation, type, begin, end - begin, left, right, low, 0];
                    node.CopyTo(words, 42 + index * 8);
                }
            }
        }
    }
}
