// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Diagnostics;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConditionalExpressionTests
{
    [Fact]
    public void ConditionalIsRightAssociativeAndBelowBinaryOperators()
    {
        const string Source = "int32 Main() { return true || false ? 1 + 2 : false ? 4 : 5; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        var choices = tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ConditionalExpression).ToArray();
        Assert.Equal(2, choices.Length);
        Assert.Equal(SyntaxKind.BinaryExpression, Assert.IsType<SyntaxNode>(choices[0].Children[0]).Kind);
        Assert.Equal(SyntaxKind.BinaryExpression, Assert.IsType<SyntaxNode>(choices[0].Children[2]).Kind);
        Assert.Same(choices[1], choices[0].Children[4]);
    }

    [Fact]
    public void SuppliedScannerTokensAndOrdinarySyntaxPreserveTheSameChoice()
    {
        const string Source = "int32 Main() { return /* choice */ true ? 42 : false ? 1 : 2; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var supplied = SyntaxTree.Parse(lexical);
        var ordinary = SyntaxTree.Parse(lexical.Source);
        Assert.False(supplied.HasErrors);
        Assert.Equal(Source, supplied.Root.ToFullString());
        Assert.Equal(ordinary.ContentId, supplied.ContentId);
        Assert.Equal<Diagnostic>(ordinary.Diagnostics, supplied.Diagnostics);
        Assert.Equal(ordinary.Root.DescendantNodes().Select(node => node.Kind),
            supplied.Root.DescendantNodes().Select(node => node.Kind));
        Assert.Contains(lexical.Tokens, token => token.Kind == TokenKind.Question);
    }

    [Theory]
    [InlineData("true ? : 1", TokenKind.Identifier)]
    [InlineData("true ? 1 2", TokenKind.Colon)]
    [InlineData("true ? 1 : ", TokenKind.Identifier)]
    public void MissingConditionalPartPreservesSourceAndFollowingStatement(string expression, TokenKind missing)
    {
        var source = "int32 Main() { int32 n = " + expression + "; return 42; }";
        var tree = SyntaxTree.Parse(source);
        Assert.True(tree.HasErrors);
        Assert.Equal(source, tree.Root.ToFullString());
        var choice = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ConditionalExpression);
        Assert.Contains(choice.DescendantTokens(), token => token.Kind == missing && token.IsMissing);
        Assert.Equal(1, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ReturnStatement));
    }

    [Theory]
    [InlineData("var value = true ? 1 : 2;")]
    [InlineData("var value = false ? null : null;")]
    [InlineData("var value = true ? int32(1) : uint32(2);")]
    [InlineData("int32 value = true ? 1 : uint32(2);")]
    public void NoLiteralFallbackOrUnapprovedSourceJoin(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { " + declaration + " return 42; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007"
            || diagnostic.Code == "TRM3014");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void OnlySelectedArmExecutesAndResultLiteralsUseTheirContext(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 Fail() { require false; return 0; }
            int32 Choose(bool first) { return first ? 42 : Fail(); }
            int32 Main() {
                int64 number = true ? 42 : 1;
                return Choose(true) + int32(number) - 42;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void GenericAndRequiredConstantsEvaluateSelectedArm(NativeRuntimePolicy policy)
    {
        const string Source = """
            [ConstEval] int32 Next(int32 n) { return n + 1; }
            [NoThrow] T Select<T>(bool first, T left, T right) where T : [Copy] {
                return first ? left : right;
            }
            int32 Main() {
                int32 selected = const (true ? Next(40) : Next(0));
                return Select<int32>(false, 0, selected + 1);
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Theory]
    [InlineData("[NoThrow] int32 F() { return false ? 1 / 0 : 42; }")]
    [InlineData("[NoThrow] int32 F() { return true ? 42 : Throw(); } int32 Throw() { require false; return 0; }")]
    public void NoThrowChecksInactiveArm(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ConditionOutputsAreAvailableOnlyOnTheirEstablishedPath(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Produce(bool success, out int32 value) {
                if (!success) { return false; }
                value = 42;
                return true;
            }
            int32 Main() {
                int32 first = Produce(true, out var produced) ? produced : 0;
                int32 second = Produce(false, out var skipped) ? skipped : 0;
                return first + second;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            bool Produce(out int32 value) { return false; }
            int32 Main() { return Produce(out var missing) ? 42 : missing; }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.True(invalid.HasErrors);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void SelectedCopyStructUsesItsIndirectReturnStorage(NativeRuntimePolicy policy)
    {
        const string Source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            Pair Pick(bool first) { return first ? new Pair(42, 0) : new Pair(0, 42); }
            int32 Main() { Pair value = Pick(true); return int32(value.First); }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Fact]
    public void VerifierRejectsMalformedConditionAndResultArms()
    {
        var program = Compile("int32 Main() { bool flag = true; return flag ? 42 : 0; }",
            NativeRuntimePolicy.FailFast);
        var function = program.Functions[program.EntryPoint];
        var result = Assert.IsType<BoundReturn>(function.Body.Statements[^1]);
        var choice = Assert.IsType<BoundConditional>(result.Value);
        foreach (var malformed in new[] {
            choice with { Condition = choice.WhenTrue },
            choice with { WhenFalse = choice.Condition },
        })
        {
            var changed = function with { Body = function.Body with {
                Statements = function.Body.Statements.SetItem(function.Body.Statements.Length - 1,
                    result with { Value = malformed }),
            } };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(program.EntryPoint, changed),
            }));
        }

    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void CheckedConditionalOutcomePublishesEitherSelectedOutput(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Fill(init ref<int32> value) { value = 42; return true; }
            int32 Pick(bool first) {
                init var value;
                require first ? Fill(init value) : Fill(init value);
                return value;
            }
            int32 Main() { return Pick(false); }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ConditionalMoveTransfersOnlySelectedOwner(NativeRuntimePolicy policy)
    {
        const string Source = """
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() { }
            }
            int32 Main() {
                Owner first = new Owner(42);
                Owner second = new Owner(1);
                Owner selected = true ? move first : move second;
                return selected.Value;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ManagedSelectedResultRemainsRootedAcrossCollection(NativeRuntimePolicy policy)
    {
        const string Source = """
            class Leaf { public int32 Value; public Leaf(int32 n) { Value = n; } }
            int32 Main() {
                require Leaf.TryCreate(42, out var first);
                require Leaf.TryCreate(1, out var second);
                Leaf selected = true ? first : second;
                Leaf? optional = false ? null : selected;
                require ManagedHeap.Collect() >= 0;
                require optional is some retained;
                return retained.Value;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(Source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ConditionalPersistsOnlyInFeatureSelectedProducerAndServicesBothConsumers(NativeRuntimePolicy policy)
    {
        const string Library = "public int32 Choose(bool first) { return first ? 42 : 1; }";
        const string Consumer = "int32 Main() { return Choose(true); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Library), (BoundRuntimePolicy)policy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producerProgram = Assert.IsType<BoundProgram>(library.Program);
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Choose"]);
        Assert.Equal(52, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var forgedOldReader = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forgedOldReader.AsSpan(4, 4), 40);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedOldReader));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer), reference,
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.Equal(52, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Choose"], "Conditional", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(nativeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeConsumerCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }
}
