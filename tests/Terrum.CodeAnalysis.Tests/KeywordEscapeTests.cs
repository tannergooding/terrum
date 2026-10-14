// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class KeywordEscapeTests
{
    // Independent vocabulary fixture: do not derive expected permission from the implementation catalog.
    private const string Vocabulary = "using namespace public internal private struct class interface enum union alias type partial extension extern static operator get set init field this where const void var unique own move mut ref write pin implicit params if else switch default when is some and or not while do for in break continue return yield async await require ensure try catch finally throw defer unsafe policy true false null new via";

    public static IEnumerable<object[]> Keywords => Vocabulary.Split(' ').Select(word => new object[] { word });
    [TestCaseSource(nameof(Keywords))]
    public void EveryApprovedEscapeIsLosslessAndBinds(string keyword)
    {
        var spelling = "@" + keyword;
        var source = "/* heading */ " + spelling + " // trailing";
        var result = Lexer.Lex(SourceText.From(source));
        Assert.That(result.Diagnostics, Is.Empty);
        var token = result.Tokens[0];
        Assert.That(token.Kind, Is.EqualTo(TokenKind.Identifier));
        Assert.That(token.Text, Is.EqualTo(spelling));
        Assert.That(token.Name, Is.EqualTo(keyword));
        Assert.That(token.Span, Is.EqualTo(new TextSpan(14, spelling.Length)));
        Assert.That(token.FullSpan, Is.EqualTo(new TextSpan(0, 14 + spelling.Length)));
        var tree = SyntaxTree.Parse(result);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var compilation = Compilation.Create(SyntaxTree.Parse($"int32 Main() {{ int32 {spelling} = 42; return {spelling}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task NativeScannerRecognizesTheIndependentCatalogAndBoundaryCasesAsync()
    {
        var words = Vocabulary.Split(' ');
        Assert.That(IdentifierName.EscapableKeywords, Is.EqualTo(words));
        await ScannerTests.CheckScannerAsync(string.Join(' ', words.Select(word => "@" + word)));
        await ScannerTests.CheckScannerAsync("@ @ await @_ @ordinaryName @int32 @out @foreach @abstract @Await @await_ @await\u0301 @await\u200C @aw\u0061it @\U00010400 @a\u00E9 @ae\u0301 @\uD800 /* trailing */ @// line\nawait");
    }
    [TestCase("@ordinaryName")]
    [TestCase("@int32")]
    [TestCase("@_")]
    [TestCase("@out")]
    [TestCase("@foreach")]
    [TestCase("@abstract")]
    [TestCase("@Await")]
    [TestCase("@await_")]
    [TestCase("@await\u0301")]
    [TestCase("@await\u200C")]
    [TestCase("@\U00010400")]
    public void InvalidEscapesAreOneBadTokenWithTheOriginalDiagnosticSpan(string spelling)
    {
        var result = Lexer.Lex(SourceText.From(spelling + " /* following */ ordinary"));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.Bad));
        Assert.That(result.Tokens[0].Text, Is.EqualTo(spelling));
        Assert.That(global::System.Linq.Enumerable.Single(result.Diagnostics).Span, Is.EqualTo(new TextSpan(0, spelling.Length)));
        Assert.That(result.Tokens[1].Text, Is.EqualTo("ordinary"));
        Assert.That(string.Concat(result.Tokens[1].LeadingTrivia.Select(item => item.Text)), Is.EqualTo(" /* following */ "));
    }
    [TestCase("@")]
    [TestCase("@ await")]
    [TestCase("@/* comment */await")]
    [TestCase("@\nawait")]
    public void IncompleteEscapesDoNotConsumeFollowingTrivia(string source)
    {
        var result = Lexer.Lex(SourceText.From(source));
        Assert.That(result.Tokens[0].Text, Is.EqualTo("@"));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.Bad));
        Assert.That(global::System.Linq.Enumerable.Single(result.Diagnostics).Span, Is.EqualTo(new TextSpan(0, 1)));
        Assert.That(SyntaxTree.Parse(result).Root.ToFullString(), Is.EqualTo(source));
    }

    [Test]
    public void LongInvalidEscapeRetainsItsCompleteSourceRange()
    {
        var spelling = "@" + new string('a', 8_000);
        var result = Lexer.Lex(SourceText.From(spelling));
        Assert.That(result.Tokens[0].Kind, Is.EqualTo(TokenKind.Bad));
        Assert.That(result.Tokens[0].Span, Is.EqualTo(new TextSpan(0, spelling.Length)));
        Assert.That(SyntaxTree.Parse(result).Root.ToFullString(), Is.EqualTo(spelling));
    }

    [Test]
    public async Task ManyInvalidEscapesAdaptWithAllDiagnosticsAsync()
    {
        var source = string.Join(' ', Enumerable.Repeat("@ordinaryName", 256));
        await ScannerTests.CheckScannerAsync(source);
    }
    [TestCase("int32 Main() { int32 @const = 42; return const; }")]
    [TestCase("int32 Main() { int32 const = 42; return @const; }")]
    [TestCase("int32 @await(int32 @return) { return @return; } int32 Main() { return @await(42); }")]
    [TestCase("namespace @namespace; int32 @await() { return 42; } int32 Main() { return @namespace.@await(); }")]
    [TestCase("struct @struct { public int32 @return; public @struct(int32 @if) { @return = @if; } public int32 @await() { return @return; } } int32 Main() { var @new = new @struct(42); return @new.@await(); }")]
    [TestCase("struct @class { public int32 @field; public @class(int32 @return) { @field = @return; } public int32 @get { get { return @field; } } } int32 Main() { var @new = new @class(42); return @new.@get; }")]
    [TestCase("int32 Main() { mut int32 @while = 0; @const: while (@while < 1) { @while = 42; break const; } return @while; }")]
    [TestCase("int32 Main() { mut int32 @while = 0; const: while (@while < 42) { @while = @while + 1; continue @const; } return @while; }")]
    [TestCase("class @class { public int32 @field; public @class(int32 @return) { @field = @return; } public int32 @get { get { return @field; } } } int32 Main() { require @class.TryCreate(42, out var @new); return @new.@get; }")]
    public void NameRoutesExecuteWithUnderlyingIdentity(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(), NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }
    [TestCase("int32 Main(int32 const, int32 @const) { return 0; }","TRM3004")]
    [TestCase("int32 Main() { int32 const = 0; int32 @const = 1; return 0; }","TRM3004")]
    [TestCase("int32 const() { return 0; } int32 @const() { return 1; } int32 Main() { return 0; }","TRM3003")]
    public void DuplicateNamesUseNormalizedIdentity(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void EscapedProtocolsGenericFormalsAndInferenceExecute()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "samples", "native", "static-protocol-quadratic.terrum"));
        Dictionary<string, string> replacements = new(StringComparer.Ordinal) {
            ["Addition"] = "@interface",
            ["Multiplication"] = "@union",
            ["Meters"] = "@struct",
            ["Seconds"] = "@class",
            ["TSelf"] = "@this",
            ["TOther"] = "@some",
            ["TResult"] = "@return",
            ["T"] = "@type",
            ["EvaluateQuadratic"] = "@await",
            ["Value"] = "@field",
        };
        var tokens = Lexer.Lex(SourceText.From(source)).Tokens;
        foreach (var token in tokens.Reverse())
        {
            if (token.Kind == TokenKind.Identifier && replacements.TryGetValue(token.Text, out var replacement))
            {
                source = source.Remove(token.Span.Start, token.Span.Length).Insert(token.Span.Start, replacement);
            }
        }
        foreach (var explicitArguments in new[] { true, false })
        {
            var selected = explicitArguments ? source
                : source.Replace("@await<@struct>", "@await", StringComparison.Ordinal)
                    .Replace("@await<@class>", "@await", StringComparison.Ordinal);
            var compilation = Compilation.Create(SyntaxTree.Parse(selected));
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), NativeRuntimePolicy.FailFast), Is.EqualTo(46));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void HandAuthoredFrontendProjectionAuditsNormalizedNames(bool duplicate)
    {
        var text = duplicate
            ? "public int32 @await(int32 const) { mut var @const = const; return const; }"
            : "public int32 @await(int32 @some) { mut var @const = some; return const; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 1;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[11] = text.IndexOf(duplicate ? "const)" : "@some)", StringComparison.Ordinal);
        words[12] = 5;
        words[37] = 2;
        words[38] = -1;
        words[39] = 1;
        words[40] = -1;
        words[298] = 1;
        words[299] = 1;
        words[300] = 1;
        words[301] = 1;
        words[302] = text.IndexOf("@const", StringComparison.Ordinal);
        words[303] = 6;
        words[332] = 1;
        words[333] = 2;
        words[334] = 0;
        words[335] = text.IndexOf("mut var", StringComparison.Ordinal);
        var initial = text.IndexOf(duplicate ? "const;" : "some;", words[335], StringComparison.Ordinal);
        words[336] = initial + (duplicate ? 6 : 5) - words[335];
        Node(0, initial, duplicate ? 5 : 4, 1);
        Node(1, text.LastIndexOf("const;", StringComparison.Ordinal), 5, 2);
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        if (duplicate)
        {
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, bytes));
        }
        else
        {
            var program = ScalarFrontendHost.BindLibrary(source, bytes, BoundRuntimePolicy.FailFast);
            Assert.That(global::System.Linq.Enumerable.Single(program.Functions).Name, Is.EqualTo("await"));
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("@const", "@int32", StringComparison.Ordinal)), bytes));
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("const;", "await;", StringComparison.Ordinal)), bytes));
        }

        void Node(int index, int start, int length, int slot)
        {
            var offset = 42 + index * 8;
            words[offset] = 0;
            words[offset + 1] = 1;
            words[offset + 2] = start;
            words[offset + 3] = length;
            words[offset + 4] = slot;
            words[offset + 5] = -1;
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void CompactFrontendProjectionRetainsUseSiteEscapeExtents(bool binary)
    {
        var text = binary
            ? "int32 @await(int32 @const, int32 some) { return const + @some; }"
            : "int32 @await(int32 @const) { return const; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[5] = 1;
        words[6] = binary ? 2 : 1;
        words[7] = 1;
        words[8] = binary ? 1 : 0;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal);
        words[10] = 6;
        words[11] = text.IndexOf("@const", StringComparison.Ordinal);
        words[12] = 6;
        if (binary)
        {
            words[13] = text.IndexOf("some)", StringComparison.Ordinal);
            words[14] = 4;
        }
        words[22] = 1;
        words[23] = binary ? (int)TokenKind.Plus : 0;
        words[24] = 1;
        words[25] = binary ? 2 : 0;
        words[26] = text.IndexOf("const", text.IndexOf("return", StringComparison.Ordinal), StringComparison.Ordinal);
        words[27] = text.IndexOf(';', words[26]) - words[26];
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        var program = ScalarFrontendHost.BindLibrary(SourceText.From(text), bytes, BoundRuntimePolicy.FailFast);
        var body = ((global::System.Func<BoundBlock>)(() => { var __nunitActual2 = global::System.Linq.Enumerable.Single(program.Functions).Body; Assert.That(__nunitActual2, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual2!; }))();
        var expression = ((global::System.Func<BoundReturn>)(() => { var __nunitActual3 = global::System.Linq.Enumerable.Single(body.Statements); Assert.That(__nunitActual3, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual3!; }))().Value;
        if (binary)
        {
            var sum = ((global::System.Func<BoundBinary>)(() => { var __nunitActual4 = expression; Assert.That(__nunitActual4, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual4!; }))();
            Assert.That(text.Substring(sum.Left.Span.Start, sum.Left.Span.Length), Is.EqualTo("const"));
            Assert.That(text.Substring(sum.Right.Span.Start, sum.Right.Span.Length), Is.EqualTo("@some"));
        }
        else
        {
            var read = ((global::System.Func<BoundRead>)(() => { var __nunitActual5 = expression; Assert.That(__nunitActual5, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual5!; }))();
            Assert.That(text.Substring(read.Span.Start, read.Span.Length), Is.EqualTo("const"));
        }
    }
    [TestCase("@if (true) { return 42; }")]
    [TestCase("@const 42;")]
    [TestCase("if (value is @some present) { return 42; }")]
    public void EscapingSuppressesKeywordPositions(string body)
    {
        var tree = SyntaxTree.Parse("int32 Main() { " + body + " return 0; }");
        Assert.That(tree.Diagnostics, Is.Not.Empty);
    }
    [TestCase("@ordinaryName")]
    [TestCase("@ await")]
    [TestCase("@int32")]
    [TestCase("@out")]
    [TestCase("@_")]
    [TestCase("_")]
    [TestCase("@await_")]
    [TestCase("x@await")]
    public void HandAuthoredIdentifierRecordsCannotAuthorizeInvalidNames(string text)
    {
        var source = SourceText.From(text);
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)TokenKind.Identifier);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), text.Length);
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadTokens(source, 2, bytes));
    }

    [Test]
    public void HistoricalSplitEscapeRecordsRemainRawButCannotMasqueradeAsCurrentInput()
    {
        var source = SourceText.From("@await");
        var tokens = Words([(int)TokenKind.Bad, 0, 1, (int)TokenKind.AwaitKeyword, 1, 5,
            (int)TokenKind.EndOfFile, 6, 0]);
        var diagnostics = Words([1, 1, 0, 1]);
        Assert.That(ScannerHost.ReadTokens(source, 3, tokens).Length, Is.EqualTo(3));
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadLexResult(source, 3, tokens, Words([0]), diagnostics));
        var incomplete = Words([(int)TokenKind.Identifier, 0, 6, (int)TokenKind.Identifier, 6, 1,
            (int)TokenKind.EndOfFile, 7, 0]);
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadTokens(SourceText.From("@await_"), 3, incomplete));

        static byte[] Words(int[] words)
        {
            var bytes = new byte[words.Length * 4];
            for (var index = 0; index < words.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
            }
            return bytes;
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PersistedExportsServiceUnchangedConsumersAsync(NativeRuntimePolicy policy)
    {
        BoundProgram Producer(string name)
        {
            var namespaceName = name.StartsWith('@') ? "@some" : "some";
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"namespace {namespaceName}; public int32 {name}() {{ return 42; }}"), (BoundRuntimePolicy)policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            return ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        }
        var escaped = Producer("@const");
        var ordinary = Producer("const");
        var producer = ModuleCompiler.CreateProducer(escaped, ["some.const"]);
        var replacement = ModuleCompiler.CreateProducer(ordinary, ["some.const"]);
        const string Client = "using some; int32 Main() { return const(); }";
        var client = Compilation.Create(SyntaxTree.Parse(Client), ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.That(client.HasErrors, Is.False, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = client.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(42));
        var nativeProducer = NativeModuleCompiler.CreateProducer(escaped, ["some.const"], "KeywordEscapes", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(ordinary, ["some.const"], "KeywordEscapes", policy);
        var nativeClient = Compilation.Create(SyntaxTree.Parse(Client),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), (BoundRuntimePolicy)policy);
        Assert.That(nativeClient.HasErrors, Is.False, string.Join(Environment.NewLine, nativeClient.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = nativeClient.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement], TimeSpan.FromSeconds(10), policy), Is.EqualTo(42));
    }
}
