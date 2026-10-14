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

public sealed class OperatorRunTests
{
    public static IEnumerable<object[]> InvalidRuns =>
        new[] { "+++", "!!", "+-", "===", ">>+", "!~", "~!", "><", "<>", "<>>", "=:",
            "::", ".+", "...", "....", "??.", "?.?", "&&&", "|||", "**", "%%", "->",
            "??==", ">>>>=", ">>=>", "/+", new string('!', 129) }
            .Select(text => new object[] { text, "TRM0011" });
    [TestCaseSource(nameof(InvalidRuns))]
    public void InvalidRunIsOneLosslessBadTokenWithWholeRawDiagnostic(string run, string code)
    {
        foreach (var prefix in new[] { "", "\U00010400/* gap */", "@await\n", "1e-2 " })
        {
            var text = prefix + run + " x";
            var lexed = Lexer.Lex(SourceText.From(text));
            var bad = global::System.Linq.Enumerable.Single(lexed.Tokens.Where(token => token.Kind == TokenKind.Bad));
            Assert.That(bad.Text, Is.EqualTo(run));
            Assert.That(bad.Span, Is.EqualTo(new TextSpan(prefix.Length, run.Length)));
            var diagnostic = global::System.Linq.Enumerable.Single(lexed.Diagnostics);
            Assert.That(diagnostic.Code, Is.EqualTo(code));
            Assert.That(diagnostic.Span, Is.EqualTo(bad.Span));
            foreach (var tree in new[] { SyntaxTree.Parse(text), SyntaxTree.Parse(lexed) })
            {
                Assert.That(tree.HasErrors, Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,item => item.Code == code && item.Span == bad.Span),Is.True);
                Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
            }
        }
    }

    public static IEnumerable<object[]> DefinedSpellings =>
        new[] { ".", ":", "?", "??", "??=", "+", "++", "-", "--", "*", "/", "%", "!", "~",
            "=", "==", "=>", "!=", "<", "<=", ">", ">=", "&", "&&", "|", "||", "^",
            "<<", ">>", ">>>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>=",
            "..", "..^", "?." }
            .Select(text => new object[] { text });
    [TestCaseSource(nameof(DefinedSpellings))]
    public void DefinedImplementedSpellingRetainsItsExistingAtomicToken(string spelling)
    {
        var lexed = Lexer.Lex(SourceText.From(spelling));
        Assert.That(lexed.Diagnostics, Is.Empty);
        Assert.That(lexed.Tokens.Length, Is.EqualTo(2));
        Assert.That(lexed.Tokens[0].Kind, Is.Not.EqualTo(TokenKind.Bad));
        Assert.That(lexed.Tokens[0].Text, Is.EqualTo(spelling));
        Assert.That(lexed.Tokens[0].Span, Is.EqualTo(new TextSpan(0, spelling.Length)));
    }
    [TestCase("int32 Main() { mut int32 x = 1; return x++ + x; }")]
    [TestCase("int32 Main() { mut int32 x = 1; return x + ++x; }")]
    [TestCase("bool F(bool x) => !(!x);")]
    [TestCase("int32 F(int32 x) => -(~x);")]
    [TestCase("int32 F(int32 x) => x >>> 1;")]
    [TestCase("int32 F(int32 x) { mut int32 y = x; y >>>= 1; return y; }")]
    [TestCase("void F() { var text = \"!!+++..^?./*\"; }")]
    [TestCase("float64 F() => 1e-2 + 2e+3;")]
    public void SeparatedGroupedAndLiteralRunsPreserveSupportedGrammar(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.Diagnostics, Is.Empty);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("+/* comment */+")]
    [TestCase("+// comment\n+")]
    [TestCase("+//** comment **//+")]
    [TestCase("+/// comment\n+")]
    public void CommentsBreakRunsBeforeOperatorClassification(string text)
    {
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.That(lexed.Diagnostics, Is.Empty);
        Assert.That(lexed.Tokens.Select(token => token.Text), Is.EqualTo(new[] { "+", "+", "" }));
        Assert.That(SyntaxTree.Parse(lexed).Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("List<List<T>>")]
    [TestCase("List<T?>")]
    [TestCase("List<T>?")]
    [TestCase("List<List<T?>>")]
    [TestCase("List<List<List<List<T>>>>")]
    [TestCase("List<List<List<List<List<T?>>>>?>")]
    public void OnlyActuallyConsumedGenericClosersAndOptionalSuffixesCertifyBoundaries(string type)
    {
        foreach (var text in new[] { type + " F() { return value; }", "void F() { Get<" + type + ">(); }" })
        {
            var tree = SyntaxTree.Parse(text);
            Assert.That(tree.Diagnostics, Is.Empty);
            Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
        }
    }

    [Test]
    public void NestedGenericClosersConsumeEveryCharacterOfShiftToken()
    {
        const string source = "void F() { Get<Outer<Inner<int32>>>(); }";
        var tree = SyntaxTree.Parse(source);

        Assert.That(tree.Diagnostics, Is.Empty);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        Assert.That(tree.Root.DescendantTokens().Count(static token => token.Kind == TokenKind.Greater), Is.EqualTo(3));
    }
    [TestCase("int32 F(int32 a, int32 b) => a >?> b;",">?>")]
    [TestCase("int32 F(int32 a, int32 b) => a >>>> b;",">>>>")]
    [TestCase("Box<T, >>? F() { return value; }",">>?")]
    [TestCase("Box<T>?> F() { return value; }",">?>")]
    public void UnconsumedOrRecoveryPunctuationCannotCertifyTypeBoundaries(string text, string run)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,diagnostic => diagnostic.Code == "TRM0011"
            && diagnostic.Span == new TextSpan(text.IndexOf(run, StringComparison.Ordinal), run.Length)),Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("T?? F() { return value; }")]
    [TestCase("List<T??> F() { return value; }")]
    [TestCase("List<List<T??>> F() { return value; }")]
    public void RepeatedOptionalMarkingRetainsDedicatedRejection(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.That(global::System.Linq.Enumerable.Single(tree.Diagnostics).Code, Is.EqualTo("TRM1005"));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
    }
    [TestCase("!!",TokenKind.Identifier)]
    [TestCase("!!",TokenKind.Bang)]
    [TestCase("+++",TokenKind.PlusPlus)]
    [TestCase("x!!",TokenKind.Identifier)]
    [TestCase("\"x\"!!",TokenKind.StringLiteral)]
    [TestCase("1!!",TokenKind.NumericLiteral)]
    [TestCase("identifier!!",TokenKind.Identifier)]
    [TestCase("'!'!!",TokenKind.CharacterLiteral)]
    [TestCase(";!!",TokenKind.Semicolon)]
    [TestCase("1e-2!!",TokenKind.NumericLiteral)]
    public void IndependentlyWrittenHistoricalRawRecordsCannotHideCurrentRuns(string text, TokenKind kind)
    {
        var source = SourceText.From(text);
        var bytes = TokenRecords((kind, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        Assert.That(ScannerHost.ReadTokens(source, 2, bytes).Length, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 2, bytes, new byte[4], new byte[4]));
    }
    [TestCase("name",TokenKind.PublicKeyword)]
    [TestCase("public",TokenKind.Identifier)]
    [TestCase("1e-2",TokenKind.Bad)]
    [TestCase("1e+",TokenKind.Identifier)]
    [TestCase("'!'",TokenKind.Identifier)]
    [TestCase("\"\\q\"",TokenKind.StringLiteral)]
    public void ReusableFramingAuditPreservesSuppliedClassificationsAndOptionalTokenDiagnostics(string text, TokenKind kind)
    {
        var source = SourceText.From(text);
        var bytes = TokenRecords((kind, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        var raw = ScannerHost.ReadTokens(source, 2, bytes);
        var result = ScannerHost.ReadLexResult(source, 2, bytes, new byte[4], new byte[4]);
        Assert.That(result.Tokens.Select(token => (token.Kind, token.Span)), Is.EqualTo(raw.Select(token => (token.Kind, token.Span))));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void ReusableFramingAuditPreservesUnpairedSurrogateClassification()
    {
        var text = new string((char)0xD800, 1);
        var source = SourceText.From(text);
        var bytes = TokenRecords((TokenKind.Identifier, 0, 1), (TokenKind.EndOfFile, 1, 0));
        var raw = ScannerHost.ReadTokens(source, 2, bytes);
        var result = ScannerHost.ReadLexResult(source, 2, bytes, new byte[4], new byte[4]);
        Assert.That(result.Tokens.Select(token => (token.Kind, token.Span)), Is.EqualTo(raw.Select(token => (token.Kind, token.Span))));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
        Assert.That(result.Diagnostics, Is.Empty);

        var hiddenPunctuation = text + "!!";
        var hiddenSource = SourceText.From(hiddenPunctuation);
        var hiddenBytes = TokenRecords((TokenKind.Bad, 0, hiddenPunctuation.Length),
            (TokenKind.EndOfFile, hiddenPunctuation.Length, 0));
        Assert.That(ScannerHost.ReadTokens(hiddenSource, 2, hiddenBytes).Length, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(hiddenSource, 2, hiddenBytes, new byte[4], new byte[4]));
    }
    [TestCase("\"x\"",TokenKind.Identifier)]
    [TestCase("x",TokenKind.StringLiteral)]
    [TestCase("'x'",TokenKind.StringLiteral)]
    public void ReusableFramingAuditRetainsStringLiteralClassificationValidation(string text, TokenKind kind)
    {
        var source = SourceText.From(text);
        var bytes = TokenRecords((kind, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        Assert.That(ScannerHost.ReadTokens(source, 2, bytes).Length, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 2, bytes, new byte[4], new byte[4]));
    }

    [Test]
    public void ShiftedFramingCannotHidePunctuationWithAnUnchangedTokenCount()
    {
        var source = SourceText.From("x!!");
        var bytes = TokenRecords((TokenKind.Identifier, 0, 2), (TokenKind.Bang, 2, 1),
            (TokenKind.EndOfFile, 3, 0));
        Assert.That(ScannerHost.ReadTokens(source, 3, bytes).Length, Is.EqualTo(Lexer.Lex(source).Tokens.Length));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 3, bytes, new byte[4], new byte[4]));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ReusableCursorDiscardsTokenDiagnosticsBeforeAuditingNextTrivia(bool includeTokenDiagnostic)
    {
        var source = SourceText.From("1e+ /*\uD800*/ x");
        var tokens = TokenRecords((TokenKind.NumericLiteral, 0, 3), (TokenKind.Identifier, 10, 1),
            (TokenKind.EndOfFile, 11, 0));
        var trivia = Words(3, (int)TriviaKind.Whitespace, 3, 1, 1,
            (int)TriviaKind.MultiLineComment, 4, 5, 1, (int)TriviaKind.Whitespace, 9, 1, 1);
        var diagnostics = includeTokenDiagnostic ? Words(2, 4, 0, 3, 1, 6, 1) : Words(1, 1, 6, 1);
        var result = ScannerHost.ReadLexResult(source, 3, tokens, trivia, diagnostics);
        Assert.That(result.Diagnostics.Length, Is.EqualTo(includeTokenDiagnostic ? 2 : 1));
        Assert.That(result.Diagnostics[^1].Code, Is.EqualTo("TRM0001"));
        Assert.That(result.Diagnostics[^1].Span, Is.EqualTo(new TextSpan(6, 1)));
        Assert.That(SyntaxTree.Parse(result).Root.ToFullString(), Is.EqualTo(source.Text));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 3, tokens, trivia, new byte[4]));
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(diagnostics.Length - 8), 7);
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 3, tokens, trivia, diagnostics));
    }

    [Test]
    public void DeferredPunctuationDoesNotAffectFollowingNumericFramingOrRequiredRunDiagnostics()
    {
        var source = SourceText.From(">>>>?1e-2!!");
        var tokens = TokenRecords((TokenKind.ShiftRightUnsigned, 0, 3), (TokenKind.Greater, 3, 1),
            (TokenKind.Question, 4, 1), (TokenKind.Identifier, 5, 4), (TokenKind.Bad, 9, 2),
            (TokenKind.EndOfFile, 11, 0));
        var diagnostics = Words(1, 11, 9, 2);
        var result = ScannerHost.ReadLexResult(source, 6, tokens, new byte[4], diagnostics);
        Assert.That(result.Tokens.Select(token => token.Text), Is.EqualTo(new[] { ">>>", ">", "?", "1e-2", "!!", "" }));
        Assert.That(result.Tokens[3].Kind, Is.EqualTo(TokenKind.Identifier));
        Assert.That(global::System.Linq.Enumerable.Single(result.Diagnostics).Span, Is.EqualTo(new TextSpan(9, 2)));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 6, tokens, new byte[4], new byte[4]));
    }
    [TestCase("!!",11)]
    public void AppendedDiagnosticRecordsServiceCurrentBadRunsWithoutChangingRawRecordShape(string text, int code)
    {
        var source = SourceText.From(text);
        var tokens = TokenRecords((TokenKind.Bad, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        var diagnostics = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics, 1);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(4), code);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(12), text.Length);
        var result = ScannerHost.ReadLexResult(source, 2, tokens, new byte[4], diagnostics);
        Assert.That(result.Tokens[0].Text, Is.EqualTo(text));
        Assert.That(global::System.Linq.Enumerable.Single(result.Diagnostics).Code, Is.EqualTo($"TRM{code:0000}"));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 2, tokens, new byte[4], new byte[4]));
    }
    [TestCase("..",TokenKind.DotDot)]
    [TestCase("..^",TokenKind.DotDotCaret)]
    [TestCase("?.",TokenKind.QuestionDot)]
    public void FormerlyUnsupportedPunctuationRequiresItsCurrentTokenKind(string text, TokenKind kind)
    {
        var source = SourceText.From(text);
        var tokens = TokenRecords((TokenKind.Bad, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        var diagnostics = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics, 1);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(4), 12);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(12), text.Length);

        Assert.Throws<ArgumentException>(() =>
            ScannerHost.ReadLexResult(source, 2, tokens, new byte[4], diagnostics));

        var current = TokenRecords((kind, 0, text.Length), (TokenKind.EndOfFile, text.Length, 0));
        var lexed = ScannerHost.ReadLexResult(source, 2, current, new byte[4], new byte[4]);
        Assert.That(global::System.Linq.Enumerable.Single(lexed.Tokens.Where(token => token.Kind != TokenKind.EndOfFile)).Kind, Is.EqualTo(kind));
        Assert.That(lexed.Diagnostics, Is.Empty);
    }

    [Test]
    public void InvalidPunctuationDiagnosticHeavyMaximumSourceRecordsRequireEveryExactCodeAndSpan()
    {
        const int runs = 5461;
        var text = new StringBuilder(ScannerHost.MaximumSourceLength);
        var records = new (TokenKind Kind, int Start, int Length)[runs + 1];
        var trivia = new byte[(1 + runs * 4) * sizeof(int)];
        var diagnostics = new byte[(1 + runs * 3) * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(trivia, runs);
        BinaryPrimitives.WriteInt32LittleEndian(diagnostics, runs);
        for (var index = 0; index < runs; index++)
        {
            text.Append("!! ");
            records[index] = (TokenKind.Bad, index * 3, 2);
            var diagnosticOffset = (1 + index * 3) * sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(diagnosticOffset), 11);
            BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(diagnosticOffset + 4), index * 3);
            BinaryPrimitives.WriteInt32LittleEndian(diagnostics.AsSpan(diagnosticOffset + 8), 2);
            var triviaOffset = (1 + index * 4) * sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(trivia.AsSpan(triviaOffset), (int)TriviaKind.Whitespace);
            BinaryPrimitives.WriteInt32LittleEndian(trivia.AsSpan(triviaOffset + 4), index * 3 + 2);
            BinaryPrimitives.WriteInt32LittleEndian(trivia.AsSpan(triviaOffset + 8), index == runs - 1 ? 2 : 1);
            BinaryPrimitives.WriteInt32LittleEndian(trivia.AsSpan(triviaOffset + 12), index + 1);
        }
        text.Append(' ');
        Assert.That(text.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        records[runs] = (TokenKind.EndOfFile, text.Length, 0);
        var source = SourceText.From(text.ToString());
        var tokens = TokenRecords(records);
        var result = ScannerHost.ReadLexResult(source, records.Length, tokens, trivia, diagnostics);
        Assert.That(result.Diagnostics.Length, Is.EqualTo(runs));
        Assert.That(result.Tokens.Length, Is.EqualTo(records.Length));
        Assert.That(SyntaxTree.Parse(result).Root.ToFullString(), Is.EqualTo(source.Text));
        for (var index = 0; index < runs; index++)
        {
            Assert.That(result.Diagnostics[index].Code, Is.EqualTo("TRM0011"));
            Assert.That(result.Diagnostics[index].Span, Is.EqualTo(new TextSpan(index * 3, 2)));
        }
        var middle = runs / 2;
        var offset = (1 + middle * 3) * sizeof(int);
        var wrongCode = (byte[])diagnostics.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(wrongCode.AsSpan(offset), 1);
        Assert.Throws<ArgumentException>(() =>
            ScannerHost.ReadLexResult(source, records.Length, tokens, trivia, wrongCode));
        var wrongSpan = (byte[])diagnostics.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(wrongSpan.AsSpan(offset + 4), middle * 3 + 1);
        Assert.Throws<ArgumentException>(() =>
            ScannerHost.ReadLexResult(source, records.Length, tokens, trivia, wrongSpan));
        var wrongRunKind = (byte[])diagnostics.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(wrongRunKind.AsSpan(offset), 12);
        Assert.Throws<ArgumentException>(() =>
            ScannerHost.ReadLexResult(source, records.Length, tokens, trivia, wrongRunKind));
        var missing = new byte[diagnostics.Length - 3 * sizeof(int)];
        diagnostics.AsSpan(0, offset).CopyTo(missing);
        diagnostics.AsSpan(offset + 3 * sizeof(int)).CopyTo(missing.AsSpan(offset));
        BinaryPrimitives.WriteInt32LittleEndian(missing, runs - 1);
        Assert.Throws<ArgumentException>(() =>
            ScannerHost.ReadLexResult(source, records.Length, tokens, trivia, missing));
    }

    [Test]
    public void DeferredCompletedRecordsRetainExactHistoricalGreedyTokenization()
    {
        var source = SourceText.From(">>>>");
        var compatible = TokenRecords((TokenKind.ShiftRightUnsigned, 0, 3), (TokenKind.Greater, 3, 1),
            (TokenKind.EndOfFile, 4, 0));
        Assert.That(ScannerHost.ReadLexResult(source, 3, compatible, new byte[4], new byte[4]).Tokens.Length, Is.EqualTo(3));
        var split = TokenRecords((TokenKind.Greater, 0, 1), (TokenKind.Greater, 1, 1),
            (TokenKind.Greater, 2, 1), (TokenKind.Greater, 3, 1), (TokenKind.EndOfFile, 4, 0));
        Assert.That(ScannerHost.ReadTokens(source, 5, split).Length, Is.EqualTo(5));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 5, split, new byte[4], new byte[4]));
    }
    [TestCase(20)]
    [TestCase(51)]
    public void IndependentlyAuthoredSuccessfulProjectionCannotServiceUnlistedUnaryRuns(int version)
    {
        foreach (var unary in new[] { "-(-x)", "-~x", "-+x", "~~x" })
        {
            var text = version == 20
                ? "public int32 Value(int32 x) => " + unary + ";"
                : "public int32 Value(int32 x) { do { return " + unary + "; } while (false); }";
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
            var grouped = unary == "-(-x)";
            var root = grouped ? 3 : 2;
            var predicateNode = root + 1;
            words[37] = root + 1 + (version == 20 ? 0 : 1);
            words[38] = -1;
            words[39] = version == 20 ? root : -1;
            words[40] = -1;
            words[41] = version == 20 ? 1 : 0;
            var start = text.IndexOf(unary, StringComparison.Ordinal);
            var operand = text.IndexOf('x', start);
            var end = start + unary.Length;
            Node(0, 0, operand, operand + 1, 1);
            Node(1, unary.Contains('~') ? 63 : unary.Contains('+') ? 57 : 58, start + (unary == "-(-x)" ? 2 : 1), operand + 1, 0);
            if (grouped)
            {
                Node(2, 46, start + 1, end, 1);
            }
            Node(root, unary.StartsWith('~') ? 63 : 58, start, end, grouped ? 2 : 1);
            if (version == 51)
            {
                var predicate = text.IndexOf("false", StringComparison.Ordinal);
                int[] node = [21, 5, predicate, 5, -1, -1, 0, 0];
                node.CopyTo(words, 42 + predicateNode * 8);
                words[298] = 2;
                var loop = text.IndexOf("do {", StringComparison.Ordinal);
                var result = text.IndexOf("return", StringComparison.Ordinal);
                int[] statements = [11, 2, predicateNode, loop, text.LastIndexOf(");", StringComparison.Ordinal) + 2 - loop,
                    8, 0, root, result, end + 1 - result];
                statements.CopyTo(words, 332);
            }
            var bytes = new byte[words.Length * sizeof(int)];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
            }
            if (unary == "-(-x)")
            {
                Assert.That(ScalarFrontendHost.ReadResult(SourceText.From(text), bytes).IsValid, Is.True);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(SourceText.From(text), bytes));
            }

            void Node(int index, int operation, int begin, int finish, int left)
            {
                int[] node = [operation, 1, begin, finish - begin, left, -1, 0, 0];
                node.CopyTo(words, 42 + index * 8);
            }
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task CompiledScannerIndependentlyPublishesFullRunRecordsAndDiagnostics(NativeRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = await ScannerTests.ReadScannerSourceAsync(root);
        var text = string.Join(" \n", InvalidRuns.Select(row => (string)row[0])
            .Concat(DefinedSpellings.Select(row => (string)row[0])))
            + "\n\U00010400 !! @await +/* comment */+ \"!!+++..^?.\" 1e-2 2e+3 >>>>? >?>"
            + "\n$ \\ [+(+{+;+,+) \uFF01 \uFF0B";
        var source = SourceText.From(text);
        var buffers = ScannerHost.CreateBuffers(source);
        var compilation = Compilation.Create(SyntaxTree.Parse(scanner + "int32 Main() { return Scan(); }"),
            buffers, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var native = NativeJit.ExecuteWithData(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), policy);
        var tokens = native.Data.AsSpan(native.BufferOffsets[3], buffers[3].Values.Length * sizeof(int)).ToArray();
        var trivia = native.Data.AsSpan(native.BufferOffsets[5], buffers[5].Values.Length * sizeof(int)).ToArray();
        var diagnostics = native.Data.AsSpan(native.BufferOffsets[4], buffers[4].Values.Length * sizeof(int)).ToArray();
        var adapted = ScannerHost.ReadLexResult(source, native.ExitCode, tokens, trivia, diagnostics);
        var hosted = Lexer.Lex(source);
        Assert.That(adapted.Tokens.Select(token => (token.Kind, token.Span)), Is.EqualTo(hosted.Tokens.Select(token => (token.Kind, token.Span))));
        Assert.That(adapted.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span)), Is.EqualTo(hosted.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.Span))));
        Assert.That(adapted.Diagnostics.Where(diagnostic => diagnostic.Code is "TRM0011" or "TRM0012").ToArray(), Is.EqualTo(hosted.Diagnostics.Where(diagnostic => diagnostic.Code is "TRM0011" or "TRM0012").ToArray()));
        Assert.That(SyntaxTree.Parse(adapted).Root.ToFullString(), Is.EqualTo(text));
    }

    [Test]
    public async Task MaximumPunctuationRunRetainsExactSourceBoundAndRepeatScanRecordsAsync()
    {
        Assert.That(ScannerHost.MaximumSourceLength, Is.EqualTo(16_384));
        var text = new string('!', ScannerHost.MaximumSourceLength);
        var lexed = Lexer.Lex(SourceText.From(text));
        Assert.That(global::System.Linq.Enumerable.Single(lexed.Diagnostics).Span, Is.EqualTo(new TextSpan(0, ScannerHost.MaximumSourceLength)));
        Assert.That(lexed.Diagnostics[0].Code, Is.EqualTo("TRM0011"));
        Assert.That(lexed.Tokens.Length, Is.EqualTo(2));
        Assert.That(lexed.Tokens[0].Text, Is.EqualTo(text));
        Assert.Throws<ArgumentException>(() => ScannerHost.CreateBuffers(SourceText.From(text + "!")));
        await ScannerTests.CheckScannerAsync(text, repeat: true);
    }

    private static byte[] Words(params int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
        }
        return bytes;
    }

    private static byte[] TokenRecords(params (TokenKind Kind, int Start, int Length)[] records)
    {
        var bytes = new byte[records.Length * 3 * sizeof(int)];
        for (var index = 0; index < records.Length; index++)
        {
            var offset = index * 3 * sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), (int)records[index].Kind);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), records[index].Start);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8), records[index].Length);
        }
        return bytes;
    }
}
