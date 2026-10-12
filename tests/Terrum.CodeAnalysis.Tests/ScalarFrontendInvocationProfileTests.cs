using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

public sealed partial class ScalarFrontendInvocationProfileTests
{
    private const string TenFunctions =
        "void F0() { } void F1() { } void F2() { } void F3() { } void F4() { } "
        + "void F5() { } void F6() { } void F7() { } void F8() { } void F9() { }";

    private static readonly string[] s_scannerComponents = [
        "SourceRange", "CharacterFacts", "Utf16CodeUnits", "Utf16Scalar", "CharacterLiteral",
        "StringEscape", "Token", "ManagedTokenChain", "Scanner", "ManagedScanner",
        "TokenSnapshot", "TokenTriviaSnapshot", "WideIntegerFacts", "IntegerParsing",
        "TokenIntegerSnapshot", "TokenDiagnosticSnapshot",
    ];

    private static readonly string[] s_frontendComponents = [.. s_scannerComponents,
        "HexDigitValue", "BasedIntegerFloatLiteral", "HexFloatLiteral", "DecimalFloatLiteral",
        "IntegerFloatConversion", "Char16FloatConversion", "ScalarParserTypes",
        "ScalarFrontend", "ScalarFrontendProgram", "ScalarFrontendProjection", "ScalarFrontendProgramReader",
    ];

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompleteFrontendWithoutInvocationProfileCannotCaptureSpansAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_frontendComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 PublishAndSummarize() {
                if (!TryPublishScalarFrontend()) { return -1; }
                if (!TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 63)) { return -2; }
                return 1;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(TenFunctions);
        Assert.Equal(61, Lexer.Lex(source).Tokens.Length);
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var window = Assert.Single(program.Functions,
            static function => function.Name == "Terrum.CodeAnalysis.TryGetScalarWindowToken");
        Assert.Contains(window.Variables, static variable => variable.Type == PrimitiveType.TokenSpan);
        Assert.DoesNotContain(program.Structures,
            static structure => structure.Name == "Terrum.CodeAnalysis.TokenCursor");

        var exception = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Terrum.CodeAnalysis.PublishAndSummarize"]));
        Assert.Equal("Compilation-local span and capture metadata cannot be persisted.", exception.Message);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SummaryReaderPersistenceRejectsCapturedFrontendBuffersAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public bool Summarize(int32 wordCount, int32 maximumTokenEnd) {
                return TryDecodeScalarFrontendProgram(wordCount, maximumTokenEnd);
            }
            """).ConfigureAwait(true);
        var compilation = Compilation.Create(trees,
            ScalarFrontendHost.CreateBuffers(SourceText.From(TenFunctions)), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.DoesNotContain(program.Functions,
            static function => function.Variables.Any(static variable => variable.Type == PrimitiveType.TokenSpan));

        var exception = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateProducer(program, ["Terrum.CodeAnalysis.Summarize"]));
        Assert.Equal("Only one invocation-scoped readonly Host.Source buffer is persistable.", exception.Message);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task SummaryReaderRejectsEmptyPublicationWithoutWritingAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(["ScalarFrontendProgramReader"], """
            namespace Terrum.CodeAnalysis;
            int32 Main() {
                if (TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 63)) { return -1; }
                return 42;
            }
            """).ConfigureAwait(true);
        var source = SourceText.From(TenFunctions);
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var actual = NativeJit.ExecuteWithData(Assert.IsType<BoundProgram>(compilation.Program),
            (NativeRuntimePolicy)policy);
        Assert.Equal(42, actual.ExitCode);
        var summary = actual.Data.AsSpan(actual.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var publication = actual.Data.AsSpan(actual.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
        Assert.All(summary, static value => Assert.Equal(0, value));
        Assert.All(publication, static value => Assert.Equal(0, value));
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadProgram(source, summary, publication, policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void ScannerProfileRejectsFrontendBuffersIdentityAndCapacityChanges(BoundRuntimePolicy policy)
    {
        var source = SourceText.From(TenFunctions);
        var scanner = ScannerHost.CreateInvocationProfileBuffers(source);
        var frontend = ScalarFrontendHost.CreateBuffers(source);
        var expanded = scanner.AddRange(frontend.Skip(scanner.Length).Select(buffer => buffer with {
            IsInvocationScoped = true,
            InvocationProfileId = ScannerHost.InvocationProfileIdentity,
        }));
        var relabeled = expanded.Select(static buffer => buffer with {
            InvocationProfileId = "scalar-frontend-v88-candidate",
        }).ToImmutableArray();
        var truncatedTokens = scanner.SetItem(3, scanner[3] with {
            Values = scanner[3].Values.RemoveAt(scanner[3].Values.Length - 1),
        });
        var mismatchedSourceCapacity = scanner.SetItem(0, scanner[0] with {
            Values = scanner[0].Values.Add(' '),
        });

        foreach (var buffers in new[] { expanded, relabeled, truncatedTokens, mismatchedSourceCapacity })
        {
            var compilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return Host.Source.Length; }"), buffers, policy);
            Assert.True(compilation.HasErrors);
            Assert.Null(compilation.Program);
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task Scanner10RebindsFreshFrontendFixturesWithoutPublicationAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_scannerComponents, """
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 ScanFrontendFixture() { return Scan(); }
            """).ConfigureAwait(true);
        var seed = SourceText.From("seed");
        var buffers = ScannerHost.CreateInvocationProfileBuffers(seed);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Terrum.CodeAnalysis.ScanFrontendFixture"]);
        var alternateCompilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("different seed 123")), policy);
        Assert.False(alternateCompilation.HasErrors,
            string.Join(Environment.NewLine, alternateCompilation.Diagnostics));
        Assert.Equal(producer, ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(alternateCompilation.Program),
            ["Terrum.CodeAnalysis.ScanFrontendFixture"]));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(ScannerHost.InvocationProfileIdentity, reference.InvocationProfileId);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ScanFrontendFixture(); }
            """), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(linked.RequiresSourceGrant);
        Assert.Equal(ScannerHost.InvocationProfileIdentity, linked.InvocationProfileId);
        Assert.Equal(10, linked.Buffers.Length);
        Assert.All(linked.Buffers, static buffer => Assert.Empty(buffer.Values));

        var maximumSource = new string(' ', ScannerHost.MaximumSourceLength - TenFunctions.Length)
            + TenFunctions;
        Assert.Equal(ScannerHost.MaximumSourceLength, maximumSource.Length);
        ScannerInvocationResult? retained = null;
        foreach (var text in new[] {
            TenFunctions,
            "int32 Main() => Later(); int32 Later() => 42;",
            "void A() { } void B() { }",
            maximumSource,
            "\uD800",
            "",
            TenFunctions,
        })
        {
            var source = SourceText.From(text);
            var expected = Lexer.Lex(source);
            Assert.InRange(expected.Tokens.Length, 1, 64);
            var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.Equal(expected.Tokens.Length, actual.TokenCount);
            Assert.Equal(expected.Tokens.Select(static token => (token.Kind, token.Span)),
                actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)));
            Assert.Equal(expected.Tokens.SelectMany(static token => token.LeadingTrivia)
                .Select(static trivia => (trivia.Kind, trivia.Span)),
                actual.LexResult.Tokens.SelectMany(static token => token.LeadingTrivia)
                    .Select(static trivia => (trivia.Kind, trivia.Span)));
            Assert.Equal(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)),
                actual.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)));
            if (text.Contains("42", StringComparison.Ordinal))
            {
                Assert.Equal(new ScannerIntegerValue(14, "", false, 42),
                    Assert.Single(actual.IntegerValues));
                retained = actual;
            }
            else
            {
                Assert.Empty(actual.IntegerValues);
            }
        }

        Assert.NotNull(retained);
        Assert.Equal(17, retained.TokenCount);
        Assert.Equal(42UL, Assert.Single(retained.IntegerValues).Magnitude);
        Assert.Equal("seed", seed.Text);
        Assert.All(buffers.Where(static buffer => buffer.IsWritable),
            static buffer => Assert.All(buffer.Values, static value => Assert.Equal(0, value)));
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScannerProfile(linked,
            SourceText.From(new string(' ', ScannerHost.MaximumSourceLength + 1)),
            (NativeRuntimePolicy)policy));
    }

    private static async Task<ImmutableArray<SyntaxTree>> ReadComponentsAsync(string[] components, string entry)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var trees = new List<SyntaxTree>(components.Length + 1);
        foreach (var name in components)
        {
            var path = Path.Combine(root, "self-host", name + ".terrum");
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
            trees.Add(tree);
        }
        trees.Add(SyntaxTree.Parse(entry, "scalar-frontend-invocation-profile-entry.terrum"));
        return [.. trees];
    }
}
