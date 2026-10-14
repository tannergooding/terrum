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
using NUnit.Framework;

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
    [TestCaseSource(nameof(Boundaries))]
    public void RoleSelectedBeforeBindingUsesExactRawTokenBoundaries(
        string form, string punctuation, string left, string right, string code)
    {
        var source = form.Replace("#", left + punctuation + right, StringComparison.Ordinal);
        var span = new TextSpan(form.IndexOf('#') + left.Length, punctuation.Length);
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.That(lexed.Diagnostics, Is.Empty);
        foreach (var tree in new[] { SyntaxTree.Parse(source), SyntaxTree.Parse(lexed) })
        {
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            if (code.Length == 0)
            {
                Assert.That(tree.Diagnostics, Is.Empty);
            }
            else
            {
                var codes = code.Split('|');
                Assert.That(tree.Diagnostics.Select(static diagnostic => diagnostic.Code), Is.EqualTo(codes));
                {var __nunitItems0 = tree.Diagnostics;                Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0.Span, Is.EqualTo(span)); } });}
            }
        }
    }
    [TestCase("Node?? F() { return null; }")]
    [TestCase("Node? ? F() { return null; }")]
    [TestCase("Node?/* gap */? F() { return null; }")]
    [TestCase("Box<Box<Node>>? ? F() { return null; }")]
    public void RepeatedOptionalModifiersStillRejectWithoutChangingRoles(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,diagnostic => diagnostic.Code == "TRM1005"),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.ConditionalExpression),Is.False);
    }
    [TestCase("void F() { Get<Box<Node /* gap */ ?>>(); }")]
    [TestCase("void F() { Get<Box<Box<Node>> /* gap */ ?>(); }")]
    public void OptionalGenericArgumentsDoNotFallBackAfterAttachmentFailure(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(global::System.Linq.Enumerable.Single(tree.Diagnostics).Code, Is.EqualTo("TRM1011"));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Root.DescendantNodes(),node => node.Kind == SyntaxKind.GenericNameExpression),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("int32 Main() { return flag?")]
    [TestCase("int32 Main() { return flag ? left:")]
    [TestCase("struct S:")]
    [TestCase("T F<T>() where T:")]
    [TestCase("int32 Main() { F(name :")]
    [TestCase("Node ?")]
    public void IncompletePunctuationRetainsEverySourceUnit(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase("enum E : int32 { A = 1 }")]
    [TestCase("[param(value): Policy] void F(int32 value) { }")]
    [TestCase("void F() { var pair = (name: 1, other: 2); }")]
    public void UnsupportedColonRolesDoNotBecomeImplemented(string source) =>
        Assert.That(SyntaxTree.Parse(source).HasErrors, Is.True);

    [Test]
    public void InterfaceBaseListUsesTheDeclaredInterfaceCompositionSyntax()
    {
        const string source = "interface I : Other, Third { }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.Root.DescendantNodes().Count(static node => node.Kind == SyntaxKind.ConformanceList),
            Is.EqualTo(1));
    }

    [Test]
    public void StringInteriorsRemainOutsidePunctuationRoles()
    {
        const string source = "void F() { var text = \"flag?left:right:X8\"; }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(global::System.Linq.Enumerable.Any(tree.Tokens,token => token.Text == "\"flag?left:right:X8\""),Is.True);
    }
    [TestCase("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await:42); }")]
    [TestCase("int32 Take(int32 @await) => @await; int32 Main() { return Take(@await:/* gap */42); }")]
    [TestCase("int32 Take(int32 \U00010400) => \U00010400; int32 Main() { return Take(\U00010400:42); }")]
    public void NamedArgumentsBindByNormalizedParameterName(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var argument = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.Argument);
        Assert.That(((global::System.Func<SyntaxTokenElement>)(() => { var __nunitActual1 = argument.Children[1]; Assert.That(__nunitActual1, Is.TypeOf<SyntaxTokenElement>()); return (SyntaxTokenElement)__nunitActual1!; }))().Token.Kind, Is.EqualTo(TokenKind.Colon));
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(tree, (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), policy), Is.EqualTo(42));
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))(), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NamedOutputArgumentsKeepSourceOrderAndFormalModes(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Record(int32 value) {
                Host.Trace[0] = Host.Trace[0] * 10 + value;
                return value;
            }
            bool Fill(int32 first, out int32 result, int32 second) {
                result = first * 10 + second;
                return true;
            }
            bool Initialize(int32 value, init ref<int32> result) {
                result = value;
                return true;
            }
            int32 Main() {
                init int32 initialized;
                mut int32 filled = 0;
                if (!Fill(second: Record(2), result: out filled, first: Record(1))) {
                    return 1;
                }
                if (!Initialize(result: init initialized, value: filled)) {
                    return 2;
                }
                return filled + initialized + Host.Trace[0] - 3;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            [new HostBuffer("Trace", PrimitiveType.Int32, [0], true)], (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NamedInstanceOutputArgumentsKeepTheReceiverInItsFormalSlot(NativeRuntimePolicy policy)
    {
        const string source = """
            struct Accumulator {
                public int32 Value;
                public Accumulator(int32 first, int32 second) { Value = first * 10 + second; }
                public int32 Combine(out int32 result, int32 first, int32 second) {
                    result = Value + first * 10 + second;
                    return 0;
                }
            }
            int32 Main() {
                Accumulator value = new Accumulator(second: 2, first: 4);
                int32 status = value.Combine(second: 0, result: out var result, first: 0);
                return result + status;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), policy), Is.EqualTo(42));
    }
    [TestCase("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(unknown: 1, second: 2); }")]
    [TestCase("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(first: 1, first: 2); }")]
    [TestCase("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(second: 2, 1); }")]
    [TestCase("int32 F(int32 first, int32 second) => first + second; int32 Main() { return F(first: 1); }")]
    [TestCase("bool Fill(out int32 value) { value = 1; return true; } int32 Main() { mut int32 result = 0; Fill(value: init result); return result; }")]
    public void InvalidNamedArgumentBindingsAreRejected(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
    }
    [TestCase("int32 Main() { outer:while (true) { break outer; } return 42; }")]
    [TestCase("int32 Main() { @for:for (;;) { break @for; } return 42; }")]
    [TestCase("int32 Main() { \U00010400:do { break \U00010400; } while (true); return 42; }")]
    public void AttachedLabelsKeepUnderlyingBindingAndTransfers(string source)
    {
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), policy), Is.EqualTo(42));
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerPreservesAllCurrentPunctuationRolesAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var native = NativeJit.ExecuteWithData(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), policy);
            Assert.That(ScannerHost.ReadTokens(source, native.ExitCode, Buffer(3)), Is.EqualTo(Lexer.Lex(source).Tokens.Select(token => new ScannerToken(token.Kind, token.Span))));
            var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
            var current = SyntaxTree.Parse(adapted);
            Assert.That(current.Root.ToFullString(), Is.EqualTo(text));
            Assert.That(current.Diagnostics.ToArray(), Is.EqualTo(SyntaxTree.Parse(source).Diagnostics.ToArray()));
            failures += current.Diagnostics.Count(diagnostic =>
                diagnostic.Code is "TRM1008" or "TRM1010" or "TRM1011" or "TRM1012");

            ReadOnlySpan<byte> Buffer(int index) =>
                native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
        }
        var expectedFailures = Boundaries.Sum(row =>
            ((string)row[4]).Split('|', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.That(failures, Is.EqualTo(expectedFailures));
    }
    [TestCase(24)]
    [TestCase(51)]
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
                words[0] = 87;
                words[1] = 1;
                words[2] = 1;
                words[5] = 1;
                words[6] = 1;
                words[7] = 5;
                words[9] = text.IndexOf("Value", StringComparison.Ordinal);
                words[10] = 5;
                words[11] = text.IndexOf("b)", StringComparison.Ordinal);
                words[12] = 1;
                words[37] = version == 24 ? 4 : 5;
                words[38] = -1;
                words[39] = version == 24 ? 3 : -1;
                words[40] = -1;
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
                    Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes).IsValid, Is.True);
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
