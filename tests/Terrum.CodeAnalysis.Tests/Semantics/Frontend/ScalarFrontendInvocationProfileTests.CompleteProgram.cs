using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Semantics.Frontend;

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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CompleteFrontendModuleRebindsFreshSourcesAndAuditsPublicationAsync(BoundRuntimePolicy policy)
    {
        var trees = await ReadComponentsAsync(s_frontendComponents, FrontendProvider).ConfigureAwait(true);
        var seed = SourceText.From("seed");
        var buffers = ScalarFrontendHost.CreateInvocationProfileBuffers(seed);
        var compilation = Compilation.Create(trees, buffers, policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))(),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)), Is.EqualTo(81));
        var alternateCompilation = Compilation.Create(trees,
            ScalarFrontendHost.CreateInvocationProfileBuffers(SourceText.From("different seed 123")), policy);
        Assert.That(alternateCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, alternateCompilation.Diagnostics));
        Assert.That(ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = alternateCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]), Is.EqualTo(producer));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.InvocationProfileId, Is.EqualTo(ScalarFrontendHost.InvocationProfileIdentity));
        Assert.That(reference.InvocationProfileId, Is.Not.EqualTo(ScannerHost.InvocationProfileIdentity));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return PublishAndSummarize(); }
            """), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = consumerCompilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.Buffers.Length, Is.EqualTo(12));
        {var __nunitItems0 = linked.Buffers;        Assert.Multiple(() => { foreach (var __nunitItem3 in __nunitItems0) { Assert.That(__nunitItem3.Values, Is.Empty); } });}
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
        Assert.That(Lexer.Lex(SourceText.From(maximumTokenSource)).Tokens.Length, Is.EqualTo(64));
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
            Assert.That(expected.Tokens.Length, Is.InRange(1, ScalarFrontendHost.MaximumInvocationTokens));
            var actual = NativeJit.ExecuteWithScalarFrontendProfile(linked, source, (NativeRuntimePolicy)policy);
            Assert.That(actual.Frontend.Projection.IsValid, Is.True);
            TestAssertions.NotNull(actual.Frontend.Program);
            Assert.That(actual.Scanner.TokenCount, Is.EqualTo(expected.Tokens.Length));
            Assert.That(actual.Scanner.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            Assert.That(actual.Scanner.LexResult.Tokens.SelectMany(static token => token.LeadingTrivia)
                    .Select(static trivia => (trivia.Kind, trivia.Span)), Is.EqualTo(expected.Tokens.SelectMany(static token => token.LeadingTrivia)
                .Select(static trivia => (trivia.Kind, trivia.Span))));
            Assert.That(actual.Scanner.LexResult.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span)), Is.EqualTo(expected.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span))));
            Assert.That(actual.Summary.Length, Is.EqualTo(ScalarFrontendHost.WordCount));
            Assert.That(actual.Summary[0], Is.EqualTo(88));
            Assert.That(actual.Summary[1], Is.EqualTo(1));
            if (text == TenFunctions)
            {
                Assert.That(actual.Frontend.Projection.Functions.Length, Is.EqualTo(10));
                Assert.That(actual.Scanner.TokenCount, Is.EqualTo(61));
                retained = actual;
            }
        }
        TestAssertions.NotNull(retained);
        Assert.That(retained.Frontend.Projection.Functions.Length, Is.EqualTo(10));
        Assert.That(retained.Scanner.TokenCount, Is.EqualTo(61));
        Assert.That(retained.Summary[66], Is.EqualTo(60));
        var replacementTrees = await ReadComponentsAsync(s_frontendComponents.Reverse().ToArray(),
            FrontendProvider.Replace("return count + 1;", "return 1 + count;", StringComparison.Ordinal))
            .ConfigureAwait(true);
        var replacementCompilation = Compilation.Create(replacementTrees,
            ScalarFrontendHost.CreateInvocationProfileBuffers(SourceText.From("replacement seed")), policy);
        Assert.That(replacementCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, replacementCompilation.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = replacementCompilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))(),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        var replacementResult = NativeJit.ExecuteWithScalarFrontendProfile(
            ModuleCompiler.Link(consumer, replacement), SourceText.From(TenFunctions), (NativeRuntimePolicy)policy);
        Assert.That(replacementResult.Summary.AsEnumerable(), Is.EqualTo(retained.Summary.AsEnumerable()));
        Assert.That(replacementResult.Frontend.Projection.Functions.Length, Is.EqualTo(10));
        {var __nunitItems2 = buffers.Where(static buffer => buffer.IsWritable);        Assert.Multiple(() => { foreach (var __nunitItem6 in __nunitItems2) { {var __nunitItems1 = __nunitItem6.Values;Assert.Multiple(() => { foreach (var __nunitItem5 in __nunitItems1) { Assert.That(__nunitItem5, Is.EqualTo(0)); } });} } });}
        Assert.Throws<ArgumentException>(() =>
            NativeJit.ExecuteWithScannerProfile(linked, seed, (NativeRuntimePolicy)policy));
        Assert.Throws<ArgumentException>(() =>
            NativeJit.ExecuteWithScalarFrontendProfile(linked, seed,
                policy == BoundRuntimePolicy.FailFast
                    ? NativeRuntimePolicy.CheckedExceptions : NativeRuntimePolicy.FailFast));
        var excessiveTokenSource = "int32 Main() => " + string.Join(" + ", Enumerable.Repeat("1", 26))
            + "; int32 Other() => 1;";
        var excessiveSource = SourceText.From(excessiveTokenSource);
        Assert.That(Lexer.Lex(excessiveSource).Tokens.Length, Is.EqualTo(65));
        var excessiveExecution = NativeJit.ExecuteWithData(linked with {
            Buffers = ScalarFrontendHost.CreateInvocationProfileBuffers(excessiveSource),
            IsInvocationSourceBound = true,
        }, (NativeRuntimePolicy)policy);
        Assert.That(excessiveExecution.ExitCode, Is.EqualTo(-3));
        var excessiveProgram = excessiveExecution.Data.AsSpan(
            excessiveExecution.BufferOffsets[ScalarFrontendHost.ProgramBufferIndex],
            ScalarFrontendHost.ProgramWordCount * sizeof(int));
        var excessiveAudit = ScalarFrontendHost.ReadProgram(excessiveSource,
            new byte[ScalarFrontendHost.WordCount * sizeof(int)], excessiveProgram, policy);
        Assert.That(excessiveAudit.Projection.IsValid, Is.True);
        TestAssertions.NotNull(excessiveAudit.Program);
        Assert.That(excessiveAudit.Projection.Functions.Length, Is.EqualTo(2));
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
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task ExpandedFrontendProfileAuditsMoreThanSixtyFourTokensAsync(BoundRuntimePolicy policy)
    {
        Assert.That(ScalarFrontendHost.WordCount, Is.EqualTo(2_274));
        Assert.That(ScalarFrontendHost.SummaryTargetOffset, Is.EqualTo(226));
        Assert.That(ScalarFrontendHost.ProgramBodyWordCount, Is.EqualTo(712));
        Assert.That(ScalarFrontendHost.ProgramFrameWordCount, Is.EqualTo(780));
        Assert.That(ScalarFrontendHost.ProgramAliasScratchWordCount, Is.EqualTo(61));
        Assert.That(ScalarFrontendHost.ProgramWordCount, Is.EqualTo(25_025));

        var expandedProvider = FrontendProvider
            .Replace("count < 64 && ", "", StringComparison.Ordinal)
            .Replace("TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, 63)",
                "TryDecodeScalarFrontendProgram(Host.FrontendProgram.Length, Host.Tokens.Length / 3 - 1)",
                StringComparison.Ordinal);
        var trees = await ReadComponentsAsync(s_frontendComponents, expandedProvider).ConfigureAwait(true);
        var seed = SourceText.From("seed");
        var compilation = Compilation.Create(trees,
            ScalarFrontendHost.CreateExpandedInvocationProfileBuffers(seed), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(),
            ["Terrum.CodeAnalysis.PublishAndSummarize"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)), Is.EqualTo(81));

        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.InvocationProfileId, Is.EqualTo(ScalarFrontendHost.ExpandedInvocationProfileIdentity));
        Assert.That(reference.InvocationProfileId, Is.Not.EqualTo(ScalarFrontendHost.InvocationProfileIdentity));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            int32 Main() { return PublishAndSummarize(); }
            """), reference, policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = consumerCompilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(linked.Buffers.Length, Is.EqualTo(12));

        var expression = string.Join(" + ", Enumerable.Repeat("1", 32));
        var sixtyFourNodeSource = SourceText.From("int32 Main() { int32 first = 0; return "
            + expression + "; } void Other() { }");
        var sixtyFourNodeResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            sixtyFourNodeSource, (NativeRuntimePolicy)policy);
        Assert.That(sixtyFourNodeResult.Frontend.Projection.IsValid, Is.True);
        Assert.That(sixtyFourNodeResult.Frontend.Projection.Functions.Length, Is.EqualTo(2));
        var function = sixtyFourNodeResult.Frontend.Projection.Functions[0];
        Assert.That(function.Nodes.Length, Is.EqualTo(64));
        var sixtyFiveNodeSource = SourceText.From("int32 Main() { int32 first = 0; "
            + "int32 second = 0; return " + expression + "; } void Other() { }");
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            sixtyFiveNodeSource, (NativeRuntimePolicy)policy));

        var exactly64Tokens = CreateEmptyFunctionWithSemicolons("F0", 57);
        Assert.That(Lexer.Lex(SourceText.From(exactly64Tokens)).Tokens.Length, Is.EqualTo(64));
        var exactWindowSource = SourceText.From($"{exactly64Tokens} void F1() {{ }}");
        Assert.That(NativeJit.ExecuteWithScalarFrontendProfile(linked, exactWindowSource,
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid, Is.True);

        var over64Tokens = CreateEmptyFunctionWithSemicolons("F0", 58);
        Assert.That(Lexer.Lex(SourceText.From(over64Tokens)).Tokens.Length, Is.EqualTo(65));
        Assert.That(NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From($"{over64Tokens} void F1() {{ }}"),
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid, Is.True);

        var overflowCallSource = SourceText.From("int32 F0() { int32 local = 0; return "
            + string.Join(" + ", Enumerable.Repeat("1", 20))
            + " + F1(); } int32 F1() => 0;");
        var overflowCallResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            overflowCallSource, (NativeRuntimePolicy)policy);
        var overflowCaller = overflowCallResult.Frontend.Projection.Functions[0];
        var overflowCallIndex = overflowCaller.Nodes.IndexOf(
            overflowCaller.Nodes.Single(static node => node.Operation == 105));
        Assert.That(overflowCallIndex, Is.InRange(32, ScalarFrontendHost.MaximumNodeCount - 1));
        Assert.That(overflowCaller.CallTargets[overflowCallIndex], Is.EqualTo(1));
        Assert.That(overflowCallResult.Summary[
            ScalarFrontendHost.SummaryTargetOffset + overflowCallIndex], Is.EqualTo(2));

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
            Assert.That(source.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
            Assert.That(expected.Tokens.Length > ScalarFrontendHost.MaximumInvocationTokens, Is.True);

            var actual = NativeJit.ExecuteWithScalarFrontendProfile(linked, source,
                (NativeRuntimePolicy)policy);
            Assert.That(actual.Scanner.TokenCount, Is.EqualTo(expected.Tokens.Length));
            Assert.That(actual.Scanner.LexResult.Tokens.Select(static token => (token.Kind, token.Span)), Is.EqualTo(expected.Tokens.Select(static token => (token.Kind, token.Span))));
            Assert.That(actual.Frontend.Projection.IsValid, Is.True);
            Assert.That(actual.Frontend.Projection.Functions.Length, Is.EqualTo(functionCount));
            Assert.That(actual.Summary[0], Is.EqualTo(88));
            Assert.That(actual.Summary[1], Is.EqualTo(1));
            var first = actual.Frontend.Projection.Functions[0];
            var firstCall = first.Nodes.IndexOf(first.Nodes.Single(static node => node.Operation == 105));
            Assert.That(first.CallTargets[firstCall], Is.EqualTo(functionCount - 1));
            Assert.That(actual.Summary[ScalarFrontendHost.SummaryTargetOffset + firstCall], Is.EqualTo(functionCount));

            var last = actual.Frontend.Projection.Functions[^1];
            var lastCall = last.Nodes.IndexOf(last.Nodes.Single(static node => node.Operation == 105));
            Assert.That(last.CallTargets[lastCall], Is.EqualTo(0));
            Assert.That(actual.Summary[ScalarFrontendHost.SummaryTargetOffset
                + ((functionCount - 1) * ScalarFrontendHost.MaximumNodeCount) + lastCall], Is.EqualTo(1));
        }

        var excessiveFunctionSource = string.Join(" ", Enumerable.Range(0,
            ScalarFrontendHost.MaximumFunctions + 1).Select(index => $"void F{index}() {{ }}"));
        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From(excessiveFunctionSource), (NativeRuntimePolicy)policy));

        var longFunction = CreateLongFunction("F0", "F1");
        Assert.That(Lexer.Lex(SourceText.From(longFunction)).Tokens.Length
            > ScalarFrontendHost.MaximumInvocationTokens, Is.True);
        Assert.That(NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From($"{longFunction} int32 F1() => 0;"),
            (NativeRuntimePolicy)policy).Frontend.Projection.IsValid, Is.True);

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
        Assert.That(maximumFunctionSource.Length, Is.EqualTo(ScannerHost.MaximumSourceLength));
        Assert.That(maximumFunctionLex.Tokens.Length > ScalarFrontendHost.MaximumInvocationTokens, Is.True);
        var maximumFunctionResult = NativeJit.ExecuteWithScalarFrontendProfile(linked,
            maximumFunctionSource, (NativeRuntimePolicy)policy);
        Assert.That(maximumFunctionResult.Scanner.TokenCount, Is.EqualTo(maximumFunctionLex.Tokens.Length));
        Assert.That(maximumFunctionResult.Frontend.Projection.IsValid, Is.True);
        Assert.That(maximumFunctionResult.Frontend.Projection.Functions.Length, Is.EqualTo(maximumFunctionCount));
        Assert.That(maximumFunctionResult.Frontend.Projection.Functions[0].Locals.Length, Is.EqualTo(8));
        Assert.That(maximumFunctionResult.Frontend.Projection.Functions[0].Statements.Length, Is.InRange(1, 12));
        Assert.That(maximumFunctionResult.Frontend.Projection.Functions[0].Nodes.Length, Is.InRange(33, 64));
        Assert.That(global::System.Linq.Enumerable.Any(maximumFunctionResult.Frontend.Projection.Functions[0].Nodes,static node => node.Operation == 105),Is.True);

        Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(linked,
            SourceText.From(new string(' ', ScannerHost.MaximumSourceLength + 1)),
            (NativeRuntimePolicy)policy));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(),
                ["Terrum.CodeAnalysis.PublishAndSummarize"]);
            var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
                namespace Terrum.CodeAnalysis;
                int32 Main() { return PublishAndSummarize(); }
                """), ModuleCompiler.CreateReference(producer), policy);
            Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
            var consumer = ModuleCompiler.CreateConsumer(
                ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = consumerCompilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), producer);
            var linked = ModuleCompiler.Link(consumer, producer);
            Assert.Throws<ArgumentException>(() => NativeJit.ExecuteWithScalarFrontendProfile(
                linked, SourceText.From(source), (NativeRuntimePolicy)policy));
        }
    }
}
