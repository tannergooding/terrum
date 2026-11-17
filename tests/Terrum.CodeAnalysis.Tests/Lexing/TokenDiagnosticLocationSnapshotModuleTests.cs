using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;
using Terrum.CodeAnalysis.Tests.Text;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Lexing;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TokenDiagnosticLocationSnapshotModuleTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task PersistedDiagnosticLocationsRebindFreshScannerGrantsAsync(BoundRuntimePolicy policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var componentNames = new[] {
            "sources/Terrum.CodeAnalysis.Terrum/Text/SourceRange", "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts", "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16CodeUnits", "sources/Terrum.CodeAnalysis.Terrum/Text/Utf16Scalar", "sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterLiteral",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/StringEscape", "sources/Terrum.CodeAnalysis.Terrum/Lexing/Token", "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedTokenChain", "sources/Terrum.CodeAnalysis.Terrum/Lexing/Scanner", "sources/Terrum.CodeAnalysis.Terrum/Lexing/ManagedScanner", "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenSnapshot",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenTriviaSnapshot", "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/WideIntegerFacts", "sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerParsing", "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenIntegerSnapshot",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenDiagnosticSnapshot", "sources/Terrum.CodeAnalysis.Terrum/Text/SourceText", "sources/Terrum.CodeAnalysis.Terrum/Text/SourcePosition", "sources/Terrum.CodeAnalysis.Terrum/Text/SourceLineIndex",
            "sources/Terrum.CodeAnalysis.Terrum/Text/SourceDiagnosticLocation",
            "sources/Terrum.CodeAnalysis.Terrum/Lexing/TokenDiagnosticLocationSnapshot",
        };
        var trees = new SyntaxTree[componentNames.Length + 1];
        for (var index = 0; index < componentNames.Length; index++)
        {
            var path = TerrumProjectPaths.RepositoryFile(root, componentNames[index] + ".terrum");
            trees[index] = SyntaxTree.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(true), path);
            Assert.That(trees[index].HasErrors, Is.False, string.Join(Environment.NewLine, trees[index].Diagnostics));
        }

        trees[^1] = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return 0; }
            public int32 VerifyDiagnosticLocations() {
                int32 tokenCount = Scan();
                if (tokenCount < 1) { return -1; }
                init TokenDiagnosticLocationSnapshot diagnostics;
                init TokenDiagnosticSnapshot expected;
                if (!TokenDiagnosticLocationSnapshot.TryBuild(ref init diagnostics)
                    || !TokenDiagnosticSnapshot.TryBuild(ref init expected)) { return -2; }
                if (diagnostics.GetCount() != expected.GetCount()) { return -3; }
                mut int32 index = 0;
                while (index < expected.GetCount()) {
                    init int32 expectedCode;
                    init SourceRange expectedSpan;
                    init int32 code;
                    init SourceRange span;
                    init int32 line;
                    init int32 column;
                    init int32 expectedLine;
                    init int32 expectedColumn;
                    if (!expected.TryGet(index, ref init expectedCode,
                            ref init expectedSpan)
                        || !diagnostics.TryGet(index, ref init code,
                            ref init span, ref init line, ref init column)) {
                        return -4;
                    }
                    if (code != expectedCode || span.Start != expectedSpan.Start
                        || span.Length != expectedSpan.Length
                        || !TryGetSourceDiagnosticLocation(span.Start,
                            ref init expectedLine, ref init expectedColumn)
                        || line != expectedLine || column != expectedColumn) { return -5; }
                    index = index + 1;
                }
                return tokenCount;
            }
            """, "token-diagnostic-location-module.terrum");

        var seed = SourceText.From("seed");
        var options = NativeCoreRegistrationTests
            .CreateRegisteredCompilationOptions(policy);
        var producerCompilation = Compilation.Create(trees,
            ScannerHost.CreateInvocationProfileBuffers(seed), options);
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producerProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = producerCompilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        var producer = ModuleCompiler.CreateProducer(producerProgram,
            ["Terrum.CodeAnalysis.VerifyDiagnosticLocations"],
            TextComponentTestHelpers.CreateIdentityInputs(producerProgram,
                "TokenDiagnosticLocationProvider"));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return VerifyDiagnosticLocations(); }
                """, "token-diagnostic-location-consumer.terrum"),
            ModuleCompiler.CreateReference(producer), options);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual1 = consumerCompilation.Program;
                Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual1!;
            }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.RequiresSourceGrant, Is.True);

        var fixtures = new[] {
            "",
            "plain source",
            "\uD800",
            "\uD800\r\uD800",
            "a\r\n\uD800\n\uD800",
            "a\uD800\rx\uD800",
            "\uD800\r\n\"unterminated",
            string.Concat(Enumerable.Repeat("\uD800\r", ScannerHost.MaximumSourceLength / 2)),
        };
        foreach (var fixture in fixtures)
        {
            var source = SourceText.From(fixture);
            var expected = Lexer.Lex(source);
            var actual = NativeJit.ExecuteWithScannerProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.That(actual.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            Assert.That(actual.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)), Is.EqualTo(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span))));
        }
    }
}
