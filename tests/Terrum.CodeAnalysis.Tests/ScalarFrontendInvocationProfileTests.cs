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
using NUnit.Framework;

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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(Lexer.Lex(source).Tokens.Length, Is.EqualTo(61));
        var compilation = Compilation.Create(trees, ScalarFrontendHost.CreateBuffers(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var window = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Terrum.CodeAnalysis.TryGetScalarWindowToken");
        Assert.That(global::System.Linq.Enumerable.Any(window.Variables,static variable => variable.Type == PrimitiveType.TokenSpan),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Structures,static structure => structure.Name == "Terrum.CodeAnalysis.TokenCursor"),Is.False);

        var exception = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(program, ["Terrum.CodeAnalysis.PublishAndSummarize"]))!;
        Assert.That(exception.Message, Is.EqualTo("Compilation-local span and capture metadata cannot be persisted."));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,static function => function.Variables.Any(static variable => variable.Type == PrimitiveType.TokenSpan)),Is.False);

        var exception = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateProducer(program, ["Terrum.CodeAnalysis.Summarize"]))!;
        Assert.That(exception.Message, Is.EqualTo("Only one invocation-scoped readonly Host.Source buffer is persistable."));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var actual = NativeJit.ExecuteWithData(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(),
            (NativeRuntimePolicy)policy);
        Assert.That(actual.ExitCode, Is.EqualTo(42));
        var summary = actual.Data.AsSpan(actual.BufferOffsets[ScalarFrontendHost.BufferIndex],
            ScalarFrontendHost.WordCount * sizeof(int)).ToArray();
        var publication = actual.Data.AsSpan(actual.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int)).ToArray();
        {var __nunitItems0 = summary;        Assert.Multiple(() => { foreach (var __nunitItem3 in __nunitItems0) { Assert.That(__nunitItem3, Is.EqualTo(0)); } });}
        {var __nunitItems1 = publication;        Assert.Multiple(() => { foreach (var __nunitItem4 in __nunitItems1) { Assert.That(__nunitItem4, Is.EqualTo(0)); } });}
        Assert.Throws<ArgumentException>(() =>
            ScalarFrontendHost.ReadProgram(source, summary, publication, policy));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(),
            ["Terrum.CodeAnalysis.ScanFrontendFixture"]);
        var alternateCompilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("different seed 123")), policy);
        Assert.That(alternateCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, alternateCompilation.Diagnostics));
        Assert.That(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = alternateCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(),
            ["Terrum.CodeAnalysis.ScanFrontendFixture"]), Is.EqualTo(producer));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.InvocationProfileId, Is.EqualTo(ScannerHost.InvocationProfileIdentity));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return ScanFrontendFixture(); }
            """), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = consumerCompilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.RequiresSourceGrant, Is.True);
        Assert.That(linked.InvocationProfileId, Is.EqualTo(ScannerHost.InvocationProfileIdentity));
        Assert.That(linked.Buffers.Length, Is.EqualTo(10));
        {var __nunitItems2 = linked.Buffers;        Assert.Multiple(() => { foreach (var __nunitItem8 in __nunitItems2) { Assert.That(__nunitItem8.Values, Is.Empty); } });}

        var maximumSource = new string(' ', ScannerHost.MaximumSourceLength - TenFunctions.Length)
            + TenFunctions;
        Assert.That(maximumSource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
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
            Assert.That(expected.Tokens.Length, Is.InRange(1, 64));
            var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.That(actual.TokenCount, Is.EqualTo(expected.Tokens.Length));
            Assert.That(actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            Assert.That(actual.LexResult.Tokens.SelectMany(static token => token.LeadingTrivia)
                    .Select(static trivia => (trivia.Kind, trivia.Span)), Is.EqualTo(expected.Tokens.SelectMany(static token => token.LeadingTrivia)
                .Select(static trivia => (trivia.Kind, trivia.Span))));
            Assert.That(actual.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)), Is.EqualTo(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span))));
            if (text.Contains("42", StringComparison.Ordinal))
            {
                Assert.That(global::System.Linq.Enumerable.Single(actual.IntegerValues), Is.EqualTo(new ScannerIntegerValue(14, "", false, 42)));
                retained = actual;
            }
            else
            {
                Assert.That(actual.IntegerValues, Is.Empty);
            }
        }

        TestAssertions.NotNull(retained);
        Assert.That(retained.TokenCount, Is.EqualTo(17));
        Assert.That(global::System.Linq.Enumerable.Single(retained.IntegerValues).Magnitude, Is.EqualTo(42UL));
        Assert.That(seed.Text, Is.EqualTo("seed"));
        {var __nunitItems4 = buffers.Where(static buffer => buffer.IsWritable);        Assert.Multiple(() => { foreach (var __nunitItem10 in __nunitItems4) { {var __nunitItems3 = __nunitItem10.Values;Assert.Multiple(() => { foreach (var __nunitItem9 in __nunitItems3) { Assert.That(__nunitItem9, Is.EqualTo(0)); } });} } });}
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
            var path = SelfHostPaths.ResolveFile(root, name + ".terrum");
            var tree = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
            trees.Add(tree);
        }
        trees.Add(SyntaxTree.Parse(entry, "scalar-frontend-invocation-profile-entry.terrum"));
        return [.. trees];
    }
}
