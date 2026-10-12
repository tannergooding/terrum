using System;
using System.Buffers.Binary;
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
    private const string FrontendProvider = """
        namespace Terrum.CodeAnalysis;
        int32 Main() { return 0; }
        public int32 PublishAndSummarize() {
            if (!TryPublishScalarFrontend()) { return -1; }
            mut int32 count = 0;
            while (count < 64 && count < Host.Tokens.Length / 3) {
                if (Host.Tokens[count * 3] == 0) {
                    if (!TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 63)) { return -2; }
                    return count + 1;
                }
                count = count + 1;
            }
            return -3;
        }
        """;

    private static string CreateLongFunction(string name, string callee)
    {
        var declarations = string.Concat(Enumerable.Range(0, 8)
            .Select(static index => $"mut int32 x{index} = {index}; "));
        var expression = string.Concat(new string('(', 15), "42", new string(')', 15),
            " + 0 + 0 + 0 + 0 + 0 + 0");
        return $"int32 {name}() {{ {{ {declarations}return {expression} + {callee}(); }} }}";
    }

    private static string CreateEmptyFunctionWithSemicolons(string name, int semicolonCount) =>
        $"void {name}() {{ {new string(';', semicolonCount)} }}";

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompleteFrontendModuleRebindsFreshSourcesAndAuditsPublicationAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_frontendComponents, FrontendProvider).ConfigureAwait(true);
        var seed = SourceText.From("seed");
        var buffers = ScalarFrontendHost.CreateInvocationProfileBuffers(seed);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)));
        var alternateCompilation = Compilation.Create(trees,
            ScalarFrontendHost.CreateInvocationProfileBuffers(SourceText.From("different seed 123")), policy);
        Assert.False(alternateCompilation.HasErrors,
            string.Join(Environment.NewLine, alternateCompilation.Diagnostics));
        Assert.Equal(producer, ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(alternateCompilation.Program),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(ScalarFrontendHost.InvocationProfileIdentity, reference.InvocationProfileId);
        Assert.NotEqual(ScannerHost.InvocationProfileIdentity, reference.InvocationProfileId);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return PublishAndSummarize(); }
            """), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(12, linked.Buffers.Length);
        Assert.All(linked.Buffers, static buffer => Assert.Empty(buffer.Values));
        foreach (var index in new[] { ScalarFrontendHost.BufferIndex, ScalarFrontendHost.ProgramBufferIndex })
        {
            var truncated = linked with {
                Buffers = linked.Buffers.SetItem(index, linked.Buffers[index] with { Values = [0] }),
            };
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
                truncated, seed, (NativeRuntimePolicy)policy));
            var wrongAccess = linked with {
                Buffers = linked.Buffers.SetItem(index, linked.Buffers[index] with { IsWritable = false }),
            };
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
                wrongAccess, seed, (NativeRuntimePolicy)policy));
        }
        var wrongIdentity = linked with {
            Buffers = linked.Buffers.SetItem(0, linked.Buffers[0] with {
                InvocationProfileId = ScannerHost.InvocationProfileIdentity,
            }),
        };
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
            wrongIdentity, seed, (NativeRuntimePolicy)policy));
        ScalarFrontendInvocationResult? retained = null;
        var maximumSource = new string(' ', ScannerHost.MaximumSourceLength - TenFunctions.Length) + TenFunctions;
        var maximumTokenSource = "public int32 Main() => " + string.Join(" + ", Enumerable.Repeat("1", 25))
            + "; int32 Other() => 1;";
        Assert.Equal(64, Lexer.Lex(SourceText.From(maximumTokenSource)).Tokens.Length);
        foreach (var text in new[] {
            TenFunctions,
            "int32 Main() => Later(); int32 Later() => 42;",
            "namespace Demo.Tools; void A() { } void B() { }",
            "namespace Demo.Tools { void A() { } void B() { } }",
            "namespace Demo.Tools { int32 Main() { return Add(20, 22); } } "
                + "namespace Demo.Tools { int32 Add(int32 left, int32 right) "
                + "{ return left + right; } }",
            "namespace /* header */ Demo.\U00010400 /* end */; void A() { } void B() { }",
            "int32 Main() => Add(1, 2, 3, 4); int32 Add(int32 a, int32 b, int32 c, int32 d) => a;",
            "int32 Main() => Pick(d: 2.0, c: 'x', a: 1, b: true); "
                + "int32 Pick(int32 a, bool b, char16 c, float64 d) => a;",
            "float32 Main() => 1.25; float32 Hex() => 0x1.fp+2; "
                + "float64 Binary() => 0b101; float64 BasedHex() => 0x1f; "
                + "float64 NegativeZero() => -0.0;",
            "int32 A() { mut int32 x = 0; Fill(out x); return x; } void Fill(out int32 x) { x = 42; }",
            maximumSource,
            maximumTokenSource,
            "void A() { } void B() { }",
        })
        {
            var source = SourceText.From(text);
            var expected = Lexer.Lex(source);
            Assert.InRange(expected.Tokens.Length, 1, ScalarFrontendHost.MaximumInvocationTokens);
            var actual = NativeJit.ExecuteWithScalarFrontendProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.True(actual.Frontend.Projection.IsValid);
            Assert.NotNull(actual.Frontend.Program);
            Assert.Equal(expected.Tokens.Length, actual.Scanner.TokenCount);
            Assert.Equal(expected.Tokens.Select(static token => (token.Kind, token.Span)),
                actual.Scanner.LexResult.Tokens.Select(static token => (token.Kind, token.Span)));
            Assert.Equal(expected.Tokens.SelectMany(static token => token.LeadingTrivia)
                .Select(static trivia => (trivia.Kind, trivia.Span)),
                actual.Scanner.LexResult.Tokens.SelectMany(static token => token.LeadingTrivia)
                    .Select(static trivia => (trivia.Kind, trivia.Span)));
            Assert.Equal(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)),
                actual.Scanner.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)));
            Assert.Equal(ScalarFrontendHost.WordCount, actual.Summary.Length);
            Assert.Equal(88, actual.Summary[0]);
            Assert.Equal(1, actual.Summary[1]);
            if (text == TenFunctions)
            {
                Assert.Equal(10, actual.Frontend.Projection.Functions.Length);
                Assert.Equal(61, actual.Scanner.TokenCount);
                retained = actual;
            }
        }
        Assert.NotNull(retained);
        Assert.Equal(10, retained.Frontend.Projection.Functions.Length);
        Assert.Equal(61, retained.Scanner.TokenCount);
        Assert.Equal(60, retained.Summary[66]);
        var replacementTrees = await ReadComponentsAsync(s_frontendComponents.Reverse().ToArray(),
            FrontendProvider.Replace("return count + 1;", "return 1 + count;", StringComparison.Ordinal))
            .ConfigureAwait(true);
        var replacementCompilation = Compilation.Create(replacementTrees,
            ScalarFrontendHost.CreateInvocationProfileBuffers(SourceText.From("replacement seed")), policy);
        Assert.False(replacementCompilation.HasErrors,
            string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(replacementCompilation.Program),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        var replacementResult = NativeJit.ExecuteWithScalarFrontendProfile(
            ModuleCompiler.Link(consumer, replacement), SourceText.From(TenFunctions), (NativeRuntimePolicy)policy);
        Assert.Equal(retained.Summary.AsEnumerable(), replacementResult.Summary.AsEnumerable());
        Assert.Equal(10, replacementResult.Frontend.Projection.Functions.Length);
        Assert.All(buffers.Where(static buffer => buffer.IsWritable),
            static buffer => Assert.All(buffer.Values, static value => Assert.Equal(0, value)));
        Assert.Throws<ArgumentException>(() =>
            NativeJit.ExecuteWithScannerProfile(linked, seed, (NativeRuntimePolicy)policy));
        Assert.Throws<ArgumentException>(() =>
            NativeJit.ExecuteWithScalarFrontendProfile(linked, seed,
                policy == BoundRuntimePolicy.FailFast
                    ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast));
        var excessiveTokenSource = "int32 Main() => " + string.Join(" + ", Enumerable.Repeat("1", 26))
            + "; int32 Other() => 1;";
        var excessiveSource = SourceText.From(excessiveTokenSource);
        Assert.Equal(65, Lexer.Lex(excessiveSource).Tokens.Length);
        var excessiveExecution = NativeJit.ExecuteWithData(linked with {
            Buffers = ScalarFrontendHost.CreateInvocationProfileBuffers(excessiveSource),
            IsInvocationSourceBound = true,
        }, (NativeRuntimePolicy)policy);
        Assert.Equal(-3, excessiveExecution.ExitCode);
        var excessiveProgram = excessiveExecution.Data.AsSpan(
            excessiveExecution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int));
        var excessiveAudit = ScalarFrontendHost.ReadProgram(excessiveSource,
            new byte[ScalarFrontendHost.WordCount * sizeof(int)], excessiveProgram, policy);
        Assert.True(excessiveAudit.Projection.IsValid);
        Assert.NotNull(excessiveAudit.Program);
        Assert.Equal(2, excessiveAudit.Projection.Functions.Length);
        foreach (var text in new[] {
            "", "\uD800", "void Alone() { }", TenFunctions + " void Overflow() { }",
            string.Concat(Enumerable.Repeat("void A() { } ", 11)),
            excessiveTokenSource,
            new string(' ', ScannerHost.MaximumSourceLength + 1),
        })
        {
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
                linked, SourceText.From(text), (NativeRuntimePolicy)policy));
        }
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExpandedFrontendProfileAuditsMoreThanSixtyFourTokensAsync(BoundRuntimePolicy policy)
    {
        Assert.Equal(2_274, ScalarFrontendHost.WordCount);
        Assert.Equal(226, ScalarFrontendHost.SummaryTargetOffset);
        Assert.Equal(712, ScalarFrontendHost.ProgramBodyWordCount);
        Assert.Equal(780, ScalarFrontendHost.ProgramFrameWordCount);
        Assert.Equal(61, ScalarFrontendHost.ProgramAliasScratchWordCount);
        Assert.Equal(25_025, ScalarFrontendHost.ProgramWordCount);

        var expandedProvider = FrontendProvider
            .Replace("count < 64 && ", "", StringComparison.Ordinal)
            .Replace("TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 63)",
                "TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, Host.Tokens.Length / 3 - 1)",
                StringComparison.Ordinal);
        var trees = await ReadComponentsAsync(s_frontendComponents, expandedProvider).ConfigureAwait(true);
        var seed = SourceText.From("seed");
        var compilation = Compilation.Create(trees,
            ScalarFrontendHost.CreateExpandedInvocationProfileBuffers(seed), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)));

        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(ScalarFrontendHost.ExpandedInvocationProfileIdentity, reference.InvocationProfileId);
        Assert.NotEqual(ScalarFrontendHost.InvocationProfileIdentity, reference.InvocationProfileId);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return PublishAndSummarize(); }
            """), reference, policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(12, linked.Buffers.Length);

        var expression = string.Join(" + ", Enumerable.Repeat("1", 32));
        var sixtyFourNodeSource = SourceText.From("int32 Main() { int32 first = 0; return "
            + expression + "; } void Other() { }");
        var sixtyFourNodeResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            sixtyFourNodeSource, (NativeRuntimePolicy)policy);
        Assert.True(sixtyFourNodeResult.Frontend.Projection.IsValid);
        Assert.Equal(2, sixtyFourNodeResult.Frontend.Projection.Functions.Length);
        var function = sixtyFourNodeResult.Frontend.Projection.Functions[0];
        Assert.Equal(64, function.Nodes.Length);
        var sixtyFiveNodeSource = SourceText.From("int32 Main() { int32 first = 0; "
            + "int32 second = 0; return " + expression + "; } void Other() { }");
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            sixtyFiveNodeSource, (NativeRuntimePolicy)policy));

        var exactly64Tokens = CreateEmptyFunctionWithSemicolons("F0", 57);
        Assert.Equal(64, Lexer.Lex(SourceText.From(exactly64Tokens)).Tokens.Length);
        var exactWindowSource = SourceText.From($"{exactly64Tokens} void F1() {{ }}");
        Assert.True(NativeJit.ExecuteWithScalarFrontendProfile(linked, exactWindowSource,
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid);

        var over64Tokens = CreateEmptyFunctionWithSemicolons("F0", 58);
        Assert.Equal(65, Lexer.Lex(SourceText.From(over64Tokens)).Tokens.Length);
        Assert.True(NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From($"{over64Tokens} void F1() {{ }}"),
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid);

        var overflowCallSource = SourceText.From("int32 F0() { int32 local = 0; return "
            + string.Join(" + ", Enumerable.Repeat("1", 20))
            + " + F1(); } int32 F1() => 0;");
        var overflowCallResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            overflowCallSource, (NativeRuntimePolicy)policy);
        var overflowCaller = overflowCallResult.Frontend.Projection.Functions[0];
        var overflowCallIndex = overflowCaller.Nodes.IndexOf(
            overflowCaller.Nodes.Single(static node => node.Operation == 105));
        Assert.InRange(overflowCallIndex, 32, ScalarFrontendHost.MaximumNodeCount - 1);
        Assert.Equal(1, overflowCaller.CallTargets[overflowCallIndex]);
        Assert.Equal(2, overflowCallResult.Summary[
            ScalarFrontendHost.SummaryTargetOffset + overflowCallIndex]);

        foreach (var functionCount in new[] { 11, 20, 21, ScalarFrontendHost.MaximumFunctions })
        {
            var functions = string.Join(" ", Enumerable.Range(0, functionCount).Select(index =>
                index == 0
                    ? $"int32 F{index}() => F{functionCount - 1}();"
                    : index == functionCount - 1
                        ? $"int32 F{index}() => F0();"
                        : $"void F{index}() {{ int32 value{index} = 0; }}"));
            var maximumSource = functions
                + new string(' ', ScannerHost.MaximumSourceLength - functions.Length);
            var source = SourceText.From(maximumSource);
            var expected = Lexer.Lex(source);
            Assert.Equal(ScannerHost.MaximumSourceLength, source.Length);
            Assert.True(expected.Tokens.Length > ScalarFrontendHost.MaximumInvocationTokens);

            var actual = NativeJit.ExecuteWithScalarFrontendProfile(linked, source,
                (NativeRuntimePolicy)policy);
            Assert.Equal(expected.Tokens.Length, actual.Scanner.TokenCount);
            Assert.Equal(expected.Tokens.Select(static token => (token.Kind, token.Span)),
                actual.Scanner.LexResult.Tokens.Select(static token => (token.Kind, token.Span)));
            Assert.True(actual.Frontend.Projection.IsValid);
            Assert.Equal(functionCount, actual.Frontend.Projection.Functions.Length);
            Assert.Equal(88, actual.Summary[0]);
            Assert.Equal(1, actual.Summary[1]);
            var first = actual.Frontend.Projection.Functions[0];
            var firstCall = first.Nodes.IndexOf(first.Nodes.Single(static node => node.Operation == 105));
            Assert.Equal(functionCount - 1, first.CallTargets[firstCall]);
            Assert.Equal(functionCount, actual.Summary[ScalarFrontendHost.SummaryTargetOffset + firstCall]);

            var last = actual.Frontend.Projection.Functions[^1];
            var lastCall = last.Nodes.IndexOf(last.Nodes.Single(static node => node.Operation == 105));
            Assert.Equal(0, last.CallTargets[lastCall]);
            Assert.Equal(1, actual.Summary[ScalarFrontendHost.SummaryTargetOffset
                + ((functionCount - 1) * ScalarFrontendHost.MaximumNodeCount) + lastCall]);
        }

        var excessiveFunctionSource = string.Join(" ", Enumerable.Range(0,
            ScalarFrontendHost.MaximumFunctions + 1).Select(index => $"void F{index}() {{ }}"));
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From(excessiveFunctionSource), (NativeRuntimePolicy)policy));

        var longFunction = CreateLongFunction("F0", "F1");
        Assert.True(Lexer.Lex(SourceText.From(longFunction)).Tokens.Length
            > ScalarFrontendHost.MaximumInvocationTokens);
        Assert.True(NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From($"{longFunction} int32 F1() => 0;"),
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid);

        var maximumFunctionCount = ScalarFrontendHost.MaximumFunctions;
        var maximumFunctionProgram = new string[maximumFunctionCount];
        maximumFunctionProgram[0] = CreateLongFunction("F0", "F1");
        maximumFunctionProgram[1] = "int32 F1() => 0;";
        maximumFunctionProgram[2] = "int32 F2() => 0;";
        for (var index = 3; index < maximumFunctionProgram.Length; index++)
        {
            maximumFunctionProgram[index] = $"void F{index}() {{ }}";
        }
        var maximumFunctionText = string.Join(" ", maximumFunctionProgram);
        var maximumFunctionSource = SourceText.From(maximumFunctionText
            + new string(' ', ScannerHost.MaximumSourceLength - maximumFunctionText.Length));
        var maximumFunctionLex = Lexer.Lex(maximumFunctionSource);
        Assert.Equal(ScannerHost.MaximumSourceLength, maximumFunctionSource.Length);
        Assert.True(maximumFunctionLex.Tokens.Length > ScalarFrontendHost.MaximumInvocationTokens);
        var maximumFunctionResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            maximumFunctionSource, (NativeRuntimePolicy)policy);
        Assert.Equal(maximumFunctionLex.Tokens.Length, maximumFunctionResult.Scanner.TokenCount);
        Assert.True(maximumFunctionResult.Frontend.Projection.IsValid);
        Assert.Equal(maximumFunctionCount, maximumFunctionResult.Frontend.Projection.Functions.Length);
        Assert.Equal(8, maximumFunctionResult.Frontend.Projection.Functions[0].Locals.Length);
        Assert.InRange(maximumFunctionResult.Frontend.Projection.Functions[0].Statements.Length, 1, 12);
        Assert.InRange(maximumFunctionResult.Frontend.Projection.Functions[0].Nodes.Length, 33, 64);
        Assert.Contains(maximumFunctionResult.Frontend.Projection.Functions[0].Nodes,
            static node => node.Operation == 105);

        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From(new string(' ', ScannerHost.MaximumSourceLength + 1)),
            (NativeRuntimePolicy)policy));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompleteFrontendModuleRejectsCorruptedProgramAndSummaryAsync(BoundRuntimePolicy policy)
    {
        const string source = "int32 Main() => Next(); int32 Next() => 42; int32 Other() => 43;";
        foreach (var mutation in new[] {
            "Host.Frontend[1] = 0;",
            "Host.Frontend[Host.Frontend.Length - 1] = 1;",
            "Host.FrontendProgram[1] = 0;",
            "Host.FrontendProgram[Host.FrontendProgram.Length - 1] = 1;",
            "Host.FrontendProgram[720] = 2; Host.Frontend[226] = 3;",
        })
        {
            var trees = await ReadComponentsAsync(s_frontendComponents, FrontendProvider.Replace(
                "return count + 1;", mutation + " return count + 1;", StringComparison.Ordinal))
                .ConfigureAwait(true);
            var compilation = Compilation.Create(trees,
                ScalarFrontendHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
                ["Terrum.CodeAnalysis.PublishAndSummarize"]);
            var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return PublishAndSummarize(); }
                """), ModuleCompiler.CreateReference(producer), policy);
            Assert.False(consumerCompilation.HasErrors,
                string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
            var linked = ModuleCompiler.Link(consumer, producer);
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
                linked, SourceText.From(source), (NativeRuntimePolicy)policy));
        }
    }
}
