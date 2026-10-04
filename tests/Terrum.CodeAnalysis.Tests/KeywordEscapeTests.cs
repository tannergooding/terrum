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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class KeywordEscapeTests
{
    // Independent vocabulary fixture: do not derive expected permission from the implementation catalog.
    private const string Vocabulary = "using namespace public internal private struct class interface enum union alias type partial extension extern static operator get set init field this where const void var unique own move mut ref write pin implicit params if else switch default when is some and or not while do for in break continue return yield async await require ensure try catch finally throw defer unsafe policy true false null new via";

    public static IEnumerable<object[]> Keywords => Vocabulary.Split(' ').Select(word => new object[] { word });

    [Theory]
    [MemberData(nameof(Keywords))]
    public void EveryApprovedEscapeIsLosslessAndBinds(string keyword)
    {
        var spelling = "@" + keyword;
        var source = "/* heading */ " + spelling + " // trailing";
        var result = Lexer.Lex(SourceText.From(source));
        Assert.Empty(result.Diagnostics);
        var token = result.Tokens[0];
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(spelling, token.Text);
        Assert.Equal(keyword, token.Name);
        Assert.Equal(new TextSpan(14, spelling.Length), token.Span);
        Assert.Equal(new TextSpan(0, 14 + spelling.Length), token.FullSpan);
        var tree = SyntaxTree.Parse(result);
        Assert.Equal(source, tree.Root.ToFullString());
        var compilation = Compilation.Create(SyntaxTree.Parse($"int32 Main() {{ int32 {spelling} = 42; return {spelling}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public async Task NativeScannerRecognizesTheIndependentCatalogAndBoundaryCasesAsync()
    {
        var words = Vocabulary.Split(' ');
        Assert.Equal(words, IdentifierName.EscapableKeywords);
        await ScannerTests.CheckScannerAsync(string.Join(' ', words.Select(word => "@" + word)));
        await ScannerTests.CheckScannerAsync("@ @ await @_ @ordinaryName @int32 @out @foreach @abstract @Await @await_ @await\u0301 @await\u200C @aw\u0061it @\U00010400 @a\u00E9 @ae\u0301 @\uD800 /* trailing */ @// line\nawait");
    }

    [Theory]
    [InlineData("@ordinaryName")]
    [InlineData("@int32")]
    [InlineData("@_")]
    [InlineData("@out")]
    [InlineData("@foreach")]
    [InlineData("@abstract")]
    [InlineData("@Await")]
    [InlineData("@await_")]
    [InlineData("@await\u0301")]
    [InlineData("@await\u200C")]
    [InlineData("@\U00010400")]
    public void InvalidEscapesAreOneBadTokenWithTheOriginalDiagnosticSpan(string spelling)
    {
        var result = Lexer.Lex(SourceText.From(spelling + " /* following */ ordinary"));
        Assert.Equal(TokenKind.Bad, result.Tokens[0].Kind);
        Assert.Equal(spelling, result.Tokens[0].Text);
        Assert.Equal(new TextSpan(0, spelling.Length), Assert.Single(result.Diagnostics).Span);
        Assert.Equal("ordinary", result.Tokens[1].Text);
        Assert.Equal(" /* following */ ", string.Concat(result.Tokens[1].LeadingTrivia.Select(item => item.Text)));
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@ await")]
    [InlineData("@/* comment */await")]
    [InlineData("@\nawait")]
    public void IncompleteEscapesDoNotConsumeFollowingTrivia(string source)
    {
        var result = Lexer.Lex(SourceText.From(source));
        Assert.Equal("@", result.Tokens[0].Text);
        Assert.Equal(TokenKind.Bad, result.Tokens[0].Kind);
        Assert.Equal(new TextSpan(0, 1), Assert.Single(result.Diagnostics).Span);
        Assert.Equal(source, SyntaxTree.Parse(result).Root.ToFullString());
    }

    [Fact]
    public void LongInvalidEscapeRetainsItsCompleteSourceRange()
    {
        var spelling = "@" + new string('a', 8_000);
        var result = Lexer.Lex(SourceText.From(spelling));
        Assert.Equal(TokenKind.Bad, result.Tokens[0].Kind);
        Assert.Equal(new TextSpan(0, spelling.Length), result.Tokens[0].Span);
        Assert.Equal(spelling, SyntaxTree.Parse(result).Root.ToFullString());
    }

    [Fact]
    public async Task ManyInvalidEscapesAdaptWithAllDiagnosticsAsync()
    {
        var source = string.Join(' ', Enumerable.Repeat("@ordinaryName", 256));
        await ScannerTests.CheckScannerAsync(source);
    }

    [Theory]
    [InlineData("int32 Main() { int32 @const = 42; return const; }")]
    [InlineData("int32 Main() { int32 const = 42; return @const; }")]
    [InlineData("int32 @await(int32 @return) { return @return; } int32 Main() { return @await(42); }")]
    [InlineData("namespace @namespace; int32 @await() { return 42; } int32 Main() { return @namespace.@await(); }")]
    [InlineData("struct @struct { public int32 @return; public @struct(int32 @if) { @return = @if; } public int32 @await() { return @return; } } int32 Main() { var @new = new @struct(42); return @new.@await(); }")]
    [InlineData("struct @class { public int32 @field; public @class(int32 @return) { @field = @return; } public int32 @get { get { return @field; } } } int32 Main() { var @new = new @class(42); return @new.@get; }")]
    [InlineData("int32 Main() { mut int32 @while = 0; @const: while (@while < 1) { @while = 42; break const; } return @while; }")]
    [InlineData("int32 Main() { mut int32 @while = 0; const: while (@while < 42) { @while = @while + 1; continue @const; } return @while; }")]
    [InlineData("class @class { public int32 @field; public @class(int32 @return) { @field = @return; } public int32 @get { get { return @field; } } } int32 Main() { require @class.TryCreate(42, out var @new); return @new.@get; }")]
    public void NameRoutesExecuteWithUnderlyingIdentity(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("int32 Main(int32 const, int32 @const) { return 0; }", "TRM3004")]
    [InlineData("int32 Main() { int32 const = 0; int32 @const = 1; return 0; }", "TRM3004")]
    [InlineData("int32 const() { return 0; } int32 @const() { return 1; } int32 Main() { return 0; }", "TRM3003")]
    public void DuplicateNamesUseNormalizedIdentity(string source, string code)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void EscapedProtocolsGenericFormalsAndInferenceExecute()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "samples", "native", "static-protocol-quadratic.terrum"));
        Dictionary<string, string> replacements = new(StringComparer.Ordinal) {
            ["Addition"] = "@interface", ["Multiplication"] = "@union",
            ["Meters"] = "@struct", ["Seconds"] = "@class",
            ["TSelf"] = "@this", ["TOther"] = "@some", ["TResult"] = "@return",
            ["T"] = "@type", ["EvaluateQuadratic"] = "@await", ["Value"] = "@field",
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
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(46, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), NativeRuntimePolicy.FailFast));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandAuthoredFrontendProjectionAuditsNormalizedNames(bool duplicate)
    {
        var text = duplicate
            ? "public int32 @await(int32 const) { mut var @const = const; return const; }"
            : "public int32 @await(int32 @some) { mut var @const = some; return const; }";
        var source = SourceText.From(text);
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85; words[1] = 1; words[2] = 1; words[5] = 1;
        words[6] = 1; words[7] = 1;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal); words[10] = 6;
        words[11] = text.IndexOf(duplicate ? "const)" : "@some)", StringComparison.Ordinal);
        words[12] = 5;
        words[37] = 2; words[38] = -1; words[39] = 1; words[40] = -1;
        words[298] = 1; words[299] = 1; words[300] = 1; words[301] = 1;
        words[302] = text.IndexOf("@const", StringComparison.Ordinal); words[303] = 6;
        words[332] = 1; words[333] = 2; words[334] = 0;
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
            Assert.Equal("await", Assert.Single(program.Functions).Name);
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("@const", "@int32", StringComparison.Ordinal)), bytes));
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(
                SourceText.From(text.Replace("const;", "await;", StringComparison.Ordinal)), bytes));
        }

        void Node(int index, int start, int length, int slot)
        {
            var offset = 42 + index * 8;
            words[offset] = 0; words[offset + 1] = 1;
            words[offset + 2] = start; words[offset + 3] = length;
            words[offset + 4] = slot; words[offset + 5] = -1;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactFrontendProjectionRetainsUseSiteEscapeExtents(bool binary)
    {
        var text = binary
            ? "int32 @await(int32 @const, int32 some) { return const + @some; }"
            : "int32 @await(int32 @const) { return const; }";
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 85; words[1] = 1; words[5] = 1;
        words[6] = binary ? 2 : 1; words[7] = 1; words[8] = binary ? 1 : 0;
        words[9] = text.IndexOf("@await", StringComparison.Ordinal); words[10] = 6;
        words[11] = text.IndexOf("@const", StringComparison.Ordinal); words[12] = 6;
        if (binary)
        {
            words[13] = text.IndexOf("some)", StringComparison.Ordinal); words[14] = 4;
        }
        words[22] = 1; words[23] = binary ? (int)TokenKind.Plus : 0; words[24] = 1;
        words[25] = binary ? 2 : 0;
        words[26] = text.IndexOf("const", text.IndexOf("return", StringComparison.Ordinal), StringComparison.Ordinal);
        words[27] = text.IndexOf(';', words[26]) - words[26];
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }
        var program = ScalarFrontendHost.BindLibrary(SourceText.From(text), bytes, BoundRuntimePolicy.FailFast);
        var body = Assert.IsType<BoundBlock>(Assert.Single(program.Functions).Body);
        var expression = Assert.IsType<BoundReturn>(Assert.Single(body.Statements)).Value;
        if (binary)
        {
            var sum = Assert.IsType<BoundBinary>(expression);
            Assert.Equal("const", text.Substring(sum.Left.Span.Start, sum.Left.Span.Length));
            Assert.Equal("@some", text.Substring(sum.Right.Span.Start, sum.Right.Span.Length));
        }
        else
        {
            var read = Assert.IsType<BoundRead>(expression);
            Assert.Equal("const", text.Substring(read.Span.Start, read.Span.Length));
        }
    }

    [Theory]
    [InlineData("@if (true) { return 42; }")]
    [InlineData("@const 42;")]
    [InlineData("if (value is @some present) { return 42; }")]
    public void EscapingSuppressesKeywordPositions(string body)
    {
        var tree = SyntaxTree.Parse("int32 Main() { " + body + " return 0; }");
        Assert.NotEmpty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("@ordinaryName")]
    [InlineData("@ await")]
    [InlineData("@int32")]
    [InlineData("@out")]
    [InlineData("@_")]
    [InlineData("_")]
    [InlineData("@await_")]
    [InlineData("x@await")]
    public void HandAuthoredIdentifierRecordsCannotAuthorizeInvalidNames(string text)
    {
        var source = SourceText.From(text);
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)TokenKind.Identifier);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), text.Length);
        Assert.Throws<ArgumentException>(() => ScannerHost.ReadTokens(source, 2, bytes));
    }

    [Fact]
    public void HistoricalSplitEscapeRecordsRemainRawButCannotMasqueradeAsCurrentInput()
    {
        var source = SourceText.From("@await");
        var tokens = Words([(int)TokenKind.Bad, 0, 1, (int)TokenKind.AwaitKeyword, 1, 5,
            (int)TokenKind.EndOfFile, 6, 0]);
        var diagnostics = Words([1, 1, 0, 1]);
        Assert.Equal(3, ScannerHost.ReadTokens(source, 3, tokens).Length);
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

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PersistedExportsServiceUnchangedConsumersAsync(NativeRuntimePolicy policy)
    {
        BoundProgram Producer(string name)
        {
            var namespaceName = name.StartsWith('@') ? "@some" : "some";
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"namespace {namespaceName}; public int32 {name}() {{ return 42; }}"), (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return Assert.IsType<BoundProgram>(compilation.Program);
        }
        var escaped = Producer("@const");
        var ordinary = Producer("const");
        var producer = ModuleCompiler.CreateProducer(escaped, ["some.const"]);
        var replacement = ModuleCompiler.CreateProducer(ordinary, ["some.const"]);
        const string Client = "using some; int32 Main() { return const(); }";
        var client = Compilation.Create(SyntaxTree.Parse(Client), ModuleCompiler.CreateReference(producer), (BoundRuntimePolicy)policy);
        Assert.False(client.HasErrors, string.Join(Environment.NewLine, client.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(client.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));
        var nativeProducer = NativeModuleCompiler.CreateProducer(escaped, ["some.const"], "KeywordEscapes", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(ordinary, ["some.const"], "KeywordEscapes", policy);
        var nativeClient = Compilation.Create(SyntaxTree.Parse(Client),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), (BoundRuntimePolicy)policy);
        Assert.False(nativeClient.HasErrors, string.Join(Environment.NewLine, nativeClient.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(Assert.IsType<BoundProgram>(nativeClient.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement], TimeSpan.FromSeconds(10), policy));
    }
}
