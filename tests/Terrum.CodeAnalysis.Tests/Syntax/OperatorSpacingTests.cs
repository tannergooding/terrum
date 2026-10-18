// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Lexing;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Syntax;

public sealed class OperatorSpacingTests
{
    private const string ReturnPrefix = "int32 Main() { return ";
    private static readonly string[] Gaps = [" ", "\n", "\r\n", "/* gap */", "//** gap **//", "// gap\n", "/// gap\n"];
    private static readonly string[] Binary = ["+", "-", "*", "/", "%", "<<", ">>", ">>>", "==", "!=", "<", "<=", ">", ">=", "&", "|", "^", "&&", "||", "??"];
    private static readonly string[] Assignments = ["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>=", "??="];

    public static IEnumerable<object[]> SeparatedOperators
    {
        get
        {
            foreach (var op in Binary)
            {
                foreach (var row in Separations("int32 Main() { return @await#@return; }", op))
                {
                    yield return row;
                }
            }
            foreach (var op in Assignments)
            {
                foreach (var row in Separations("int32 Main() { value#42; return value; }", op))
                {
                    yield return row;
                }
            }
            foreach (var form in new[] {
                "int32 Main() { int32 value#42; return value; }",
                "struct Box { public int32 Value#42; }",
                "int32 Main() { _#42; return 42; }",
                "int32 Main() { for (mut int32 x#0; x < 1; x++) { } return 42; }",
                "int32 Main() { for (; true; x#42) { break; } return 42; }",
            })
            {
                foreach (var row in Separations(form, "="))
                {
                    yield return row;
                }
            }
            foreach (var form in new[] {
                "int32 Main()#42;",
                "struct Box { public int32 Value; public Box(int32 value)#Value = value; }",
                "struct Box { public static Box operator +(Box a, Box b)#a; }",
                "struct Box { public int32 Value#42; }",
                "struct Box { public int32 Value { get#42; } }",
            })
            {
                foreach (var row in Separations(form, "=>"))
                {
                    yield return row;
                }
            }
        }
    }

    private static IEnumerable<object[]> Separations(string form, string op)
    {
        foreach (var gap in Gaps)
        {
            yield return [form, op, gap, gap, false];
            yield return [form, op, "", gap, true];
            yield return [form, op, gap, "", true];
        }
        yield return [form, op, "", "", true];
    }
    [TestCaseSource(nameof(SeparatedOperators))]
    public void BinaryBindingAndArrowRolesRequireBothSeparators(string form, string op, string left, string right, bool invalid)
    {
        if (op == "/" && right.StartsWith("/", StringComparison.Ordinal))
        {
            right = " " + right;
        }
        var source = form.Replace("#", left + op + right, StringComparison.Ordinal);
        Check(source, invalid ? "TRM1008" : null, new TextSpan(form.IndexOf('#') + left.Length, op.Length));
    }

    public static IEnumerable<object[]> UnaryOperators =>
        from op in new[] { "+", "-", "!", "~", "++", "--" }
        from operand in new[] { "@await", "( /* interior */ value)", "values[0]", "\U00010400" }
        from gap in new[] { "" }.Concat(Gaps)
        select new object[] { op, operand, gap };
    [TestCaseSource(nameof(UnaryOperators))]
    public void PrefixAttachesToTheImmediateOperandToken(string op, string operand, string gap)
    {
        var source = ReturnPrefix + op + gap + operand + "; }";
        Check(source, gap.Length == 0 ? null : "TRM1009", new TextSpan(ReturnPrefix.Length, op.Length));
    }

    public static IEnumerable<object[]> PostfixOperators =>
        from op in new[] { "++", "--" }
        from operand in new[] { "@await", "(value)", "values[0]", "Get()", "\U00010400" }
        from gap in new[] { "" }.Concat(Gaps)
        select new object[] { op, operand, gap };
    [TestCaseSource(nameof(PostfixOperators))]
    public void PostfixAttachesToTheReceiversFinalToken(string op, string operand, string gap)
    {
        var source = ReturnPrefix + operand + gap + op + "; }";
        Check(source, gap.Length == 0 ? null : "TRM1009", new TextSpan(ReturnPrefix.Length + operand.Length + gap.Length, op.Length));
    }
    [TestCase("int32 Main() { return x++ + y; }")]
    [TestCase("int32 Main() { return x + ++y; }")]
    [TestCase("int32 Main() { return x-- - y; }")]
    [TestCase("int32 Main() { return x - --y; }")]
    [TestCase("int32 Main() { return -( /* interior */ value); }")]
    [TestCase("int32 Main() { return const (value + other); }")]
    [TestCase("int32 Main() { return const(value); }")]
    [TestCase("int32 Main() { return move /* word */ value; }")]
    [TestCase("int32 Main() { return flag ? left : right; }")]
    [TestCase("Node? F(Node? value) { return value; }")]
    [TestCase("int32 Main() { return F<Box<Box<int32>>>() >> 1; }")]
    [TestCase("struct Box { public static Box operator +(Box a, Box b) { return a; } }")]
    [TestCase("struct Box { public static Box operator ++(Box a) { return a; } }")]
    public void ExistingRolesAndExcludedPunctuationRemainDistinct(string source) => Check(source, null, default);
    [TestCase("int32 Main() { return value+")]
    [TestCase("int32 Main() { return - ")]
    [TestCase("int32 Main()=>")]
    [TestCase("int32 Main() { int32 value=")]
    public void InvalidIncompleteOperatorsRemainLossless(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
    }
    [TestCase(""," ")]
    [TestCase(" ","")]
    [TestCase("","")]
    [TestCase(" "," ")]
    public void HandAuthoredHistoricalBinaryProjectionChecksCurrentSourceSpacing(string left, string right)
    {
        var text = "int32 F(int32 x, int32 y) { return x" + left + "+" + right + "y; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = 1;
        words[6] = 2;
        words[7] = 1;
        words[8] = 1;
        words[9] = 6;
        words[10] = 1;
        words[11] = text.IndexOf("x,", StringComparison.Ordinal);
        words[12] = 1;
        words[13] = text.IndexOf("y)", StringComparison.Ordinal);
        words[14] = 1;
        words[22] = 1;
        words[23] = (int)TokenKind.Plus;
        words[24] = 1;
        words[25] = 2;
        words[26] = text.IndexOf("x", text.IndexOf("return", StringComparison.Ordinal), StringComparison.Ordinal);
        words[27] = text.IndexOf(';', words[26]) - words[26];
        var bytes = Words(words);
        if (left.Length != 0 && right.Length != 0)
        {
            Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes).IsValid, Is.True);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
        }
    }

    private static byte[] Words(int[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        return bytes;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerPreservesEveryOperatorRoleAndBoundaryAsync(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
        var inputs = new List<string>();
        foreach (var row in SeparatedOperators)
        {
            var op = (string)row[1];
            var right = (string)row[3];
            if (op == "/" && right.StartsWith("/", StringComparison.Ordinal))
            {
                right = " " + right;
            }
            inputs.Add(((string)row[0]).Replace("#", (string)row[2] + op + right, StringComparison.Ordinal));
        }
        inputs.AddRange(UnaryOperators.Select(row => ReturnPrefix + row[0] + (string)row[2] + row[1] + "; }"));
        inputs.AddRange(PostfixOperators.Select(row => ReturnPrefix + row[1] + (string)row[2] + row[0] + "; }"));
        var chunks = new List<string>();
        var chunk = new StringBuilder();
        foreach (var input in inputs)
        {
            if (chunk.Length + input.Length + 1 > ScannerHost.MaximumSourceLength)
            {
                chunks.Add(chunk.ToString());
                chunk.Clear();
            }
            chunk.AppendLine(input);
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
            var native = NativeJit.ExecuteWithData(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), policy);
            var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, Buffer(3), Buffer(5), Buffer(4));
            Assert.That(ScannerHost.ReadTokens(source, native.ExitCode, Buffer(3)), Is.EqualTo(Lexer.Lex(source).Tokens.Select(token => new ScannerToken(token.Kind, token.Span))));
            var current = SyntaxTree.Parse(adapted);
            Assert.That(current.Root.ToFullString(), Is.EqualTo(text));
            Assert.That(current.Diagnostics.ToArray(), Is.EqualTo(SyntaxTree.Parse(source).Diagnostics.ToArray()));
            failures += current.Diagnostics.Count(diagnostic => diagnostic.Code is "TRM1008" or "TRM1009");

            ReadOnlySpan<byte> Buffer(int index) =>
                native.Data.AsSpan(native.BufferOffsets[index], buffers[index].Values.Length * sizeof(int));
        }
        Assert.That(failures, Is.EqualTo(SeparatedOperators.Count(row => (bool)row[4])
            + UnaryOperators.Count(row => ((string)row[2]).Length != 0)
            + PostfixOperators.Count(row => ((string)row[2]).Length != 0)));
    }
    [TestCase(20)]
    public void HandAuthoredTreeProjectionChecksArrowAndPrefixSpacing(int version)
    {
        foreach (var (left, right, unary) in new[] {
            (" ", " ", ""), ("", " ", ""), (" ", "", ""),
            ("", "", ""), (" ", " ", " "), (" ", " ", "/* gap */"),
        })
        {
            var text = "public int32 Value(int32 x)" + left + "=>" + right + "-" + unary + "x;";
            var words = new int[ScalarFrontendHost.WordCount];
            words[0] = 87;
            words[1] = 1;
            words[2] = 1;
            words[5] = 1;
            words[6] = 1;
            words[7] = 1;
            words[9] = text.IndexOf("Value", StringComparison.Ordinal);
            words[10] = 5;
            words[11] = text.IndexOf("x)", StringComparison.Ordinal);
            words[12] = 1;
            words[37] = 2;
            words[38] = -1;
            words[39] = 1;
            words[40] = -1;
            words[41] = 1;
            var start = text.IndexOf('-', StringComparison.Ordinal);
            var end = text.IndexOf(';', StringComparison.Ordinal);
            int[] nodes = [
                0, 1, end - 1, 1, 1, -1, 0, 0,
                58, 1, start, end - start, 0, -1, 0, 0,
            ];
            nodes.CopyTo(words, 42);
            if (left.Length != 0 && right.Length != 0 && unary.Length == 0)
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)).IsValid, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)));
            }
        }
    }
    [TestCase(49)]
    public void HandAuthoredStructuredProjectionChecksBindingsCompoundsAndPostfix(int version)
    {
        foreach (var (binding, compound, postfix) in new[] {
            (" = ", " += ", ""), ("= ", " += ", ""), (" =", " += ", ""),
            (" = ", "+= ", ""), (" = ", " +=", ""), (" = ", " += ", "/* gap */"),
        })
        {
            var text = "public int32 Value() { mut int32 x" + binding + "4; x" + compound + "2; return x" + postfix + "++; }";
            var words = new int[ScalarFrontendHost.WordCount];
            words[0] = 87;
            words[1] = 1;
            words[2] = 1;
            words[5] = 1;
            words[9] = text.IndexOf("Value", StringComparison.Ordinal);
            words[10] = 5;
            words[37] = 6;
            words[38] = -1;
            words[39] = -1;
            words[40] = -1;
            words[298] = 3;
            words[299] = 1;
            words[300] = 1;
            words[301] = 1;
            words[302] = text.IndexOf("int32 x", StringComparison.Ordinal) + 6;
            words[303] = 1;
            var declaration = text.IndexOf("mut int32", StringComparison.Ordinal);
            var assignment = text.IndexOf("; x", StringComparison.Ordinal) + 2;
            var result = text.IndexOf("return", StringComparison.Ordinal);
            var firstEnd = text.IndexOf(';', declaration) + 1;
            var secondEnd = text.IndexOf(';', assignment) + 1;
            var lastEnd = text.LastIndexOf(';') + 1;
            Node(0, 3, firstEnd - 2, firstEnd - 1, -1, -1, 4);
            Node(1, 3, secondEnd - 2, secondEnd - 1, -1, -1, 2);
            Node(2, 0, assignment, assignment + 1, 1, -1, 0);
            Node(3, 91, assignment, secondEnd - 1, 2, 1, 0);
            Node(4, 0, result + 7, result + 8, 1, -1, 0);
            Node(5, 86, result + 7, lastEnd - 1, 4, -1, 0);
            Statement(0, 1, 1, 0, declaration, firstEnd);
            Statement(1, 9, 1, 3, assignment, secondEnd);
            Statement(2, 8, 0, 5, result, lastEnd);
            if (binding == " = " && compound == " += " && postfix.Length == 0)
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)).IsValid, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)));
            }

            void Node(int index, int operation, int start, int end, int left, int right, int low)
            {
                int[] node = [operation, 1, start, end - start, left, right, low, 0];
                node.CopyTo(words, 42 + index * 8);
            }
            void Statement(int index, int kind, int slot, int node, int start, int end)
            {
                int[] statement = [kind, slot, node, start, end - start];
                statement.CopyTo(words, 332 + index * 5);
            }
        }
    }

    [Test]
    public void HandAuthoredLatestLoopProjectionRejectsSeparatedPrefix()
    {
        foreach (var gap in new[] { "" }.Concat(Gaps))
        {
            var text = "public int32 Value() { do { return +" + gap + "42; } while (false); }";
            var words = new int[ScalarFrontendHost.WordCount];
            words[0] = 87;
            words[1] = 1;
            words[2] = 1;
            words[5] = 1;
            words[9] = text.IndexOf("Value", StringComparison.Ordinal);
            words[10] = 5;
            words[37] = 3;
            words[38] = -1;
            words[39] = -1;
            words[40] = -1;
            words[298] = 2;
            var number = text.IndexOf("42", StringComparison.Ordinal);
            var prefix = text.IndexOf('+', StringComparison.Ordinal);
            var predicate = text.IndexOf("false", StringComparison.Ordinal);
            int[] nodes = [
                3, 1, number, 2, -1, -1, 42, 0,
                57, 1, prefix, number + 2 - prefix, 0, -1, 0, 0,
                21, 5, predicate, 5, -1, -1, 0, 0,
            ];
            nodes.CopyTo(words, 42);
            var loop = text.IndexOf("do {", StringComparison.Ordinal);
            var finish = text.IndexOf(");", loop, StringComparison.Ordinal) + 2;
            var result = text.IndexOf("return", StringComparison.Ordinal);
            int[] statements = [
                11, 2, 2, loop, finish - loop,
                8, 0, 1, result, number + 3 - result,
            ];
            statements.CopyTo(words, 332);
            if (gap.Length == 0)
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)).IsValid, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), Words(words)));
            }
        }
    }

    private static void Check(string source, string? code, TextSpan span)
    {
        var lexed = Lexer.Lex(SourceText.From(source));
        Assert.That(lexed.Diagnostics, Is.Empty);
        foreach (var tree in new[] { SyntaxTree.Parse(source), SyntaxTree.Parse(lexed) })
        {
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
            if (code is null)
            {
                Assert.That(tree.Diagnostics, Is.Empty);
            }
            else
            {
                var diagnostic = global::System.Linq.Enumerable.Single(tree.Diagnostics);
                Assert.That(diagnostic.Code, Is.EqualTo(code));
                Assert.That(diagnostic.Span, Is.EqualTo(span));
            }
        }
    }
}
