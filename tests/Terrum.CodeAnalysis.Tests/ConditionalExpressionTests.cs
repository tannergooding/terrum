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
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConditionalExpressionTests
{
    [Test]
    public void ConditionalIsRightAssociativeAndBelowBinaryOperators()
    {
        const string Source = "int32 Main() { return true || false ? 1 + 2 : false ? 4 : 5; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        var choices = tree.Root.DescendantNodes().Where(node => node.Kind == SyntaxKind.ConditionalExpression).ToArray();
        Assert.That(choices.Length, Is.EqualTo(2));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual0 = choices[0].Children[0]; Assert.That(__nunitActual0, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual0!; }))().Kind, Is.EqualTo(SyntaxKind.BinaryExpression));
        Assert.That(((global::System.Func<SyntaxNode>)(() => { var __nunitActual1 = choices[0].Children[2]; Assert.That(__nunitActual1, Is.TypeOf<SyntaxNode>()); return (SyntaxNode)__nunitActual1!; }))().Kind, Is.EqualTo(SyntaxKind.BinaryExpression));
        Assert.That(choices[0].Children[4], Is.SameAs(choices[1]));
    }

    [Test]
    public void SuppliedScannerTokensAndOrdinarySyntaxPreserveTheSameChoice()
    {
        const string Source = "int32 Main() { return /* choice */ true ? 42 : false ? 1 : 2; }";
        var lexical = Lexer.Lex(SourceText.From(Source));
        var supplied = SyntaxTree.Parse(lexical);
        var ordinary = SyntaxTree.Parse(lexical.Source);
        Assert.That(supplied.HasErrors, Is.False);
        Assert.That(supplied.Root.ToFullString(), Is.EqualTo(Source));
        Assert.That(supplied.ContentId, Is.EqualTo(ordinary.ContentId));
        Assert.That(supplied.Diagnostics, Is.EqualTo(ordinary.Diagnostics));
        Assert.That(supplied.Root.DescendantNodes().Select(node => node.Kind), Is.EqualTo(ordinary.Root.DescendantNodes().Select(node => node.Kind)));
        Assert.That(global::System.Linq.Enumerable.Any(lexical.Tokens,token => token.Kind == TokenKind.Question),Is.True);
    }
    [TestCase("true ? : 1",TokenKind.Identifier)]
    [TestCase("true ? 1 2",TokenKind.Colon)]
    [TestCase("true ? 1 : ",TokenKind.Identifier)]
    public void MissingConditionalPartPreservesSourceAndFollowingStatement(string expression, TokenKind missing)
    {
        var source = "int32 Main() { int32 n = " + expression + "; return 42; }";
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.True);
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(source));
        var choice = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.ConditionalExpression);
        Assert.That(global::System.Linq.Enumerable.Any(choice.DescendantTokens(),token => token.Kind == missing && token.IsMissing),Is.True);
        Assert.That(tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ReturnStatement), Is.EqualTo(1));
    }
    [TestCase("var value = true ? 1 : 2;")]
    [TestCase("var value = false ? null : null;")]
    [TestCase("var value = true ? int32(1) : uint32(2);")]
    [TestCase("int32 value = true ? 1 : uint32(2);")]
    public void NoLiteralFallbackOrUnapprovedSourceJoin(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { " + declaration + " return 42; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"
            || diagnostic.Code == "TRM3014"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }
    [TestCase("[NoThrow] int32 F() { return false ? 1 / 0 : 42; }")]
    [TestCase("[NoThrow] int32 F() { return true ? 42 : Throw(); } int32 Throw() { require false; return 0; }")]
    public void NoThrowChecksInactiveArm(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));

        var invalid = Compilation.Create(SyntaxTree.Parse("""
            bool Produce(out int32 value) { return false; }
            int32 Main() { return Produce(out var missing) ? 42 : missing; }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(invalid.HasErrors, Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }

    [Test]
    public void VerifierRejectsMalformedConditionAndResultArms()
    {
        var program = Compile("int32 Main() { bool flag = true; return flag ? 42 : 0; }",
            NativeRuntimePolicy.FailFast);
        var function = program.Functions[program.EntryPoint];
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual2 = function.Body.Statements[^1]; Assert.That(__nunitActual2, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual2!; }))();
        var choice = ((global::System.Func<BoundConditional>)(() => { var __nunitActual3 = result.Value; Assert.That(__nunitActual3, Is.TypeOf<BoundConditional>()); return (BoundConditional)__nunitActual3!; }))();
        foreach (var malformed in new[] {
            choice with { Condition = choice.WhenTrue },
            choice with { WhenFalse = choice.Condition },
        })
        {
            var changed = function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(function.Body.Statements.Length - 1,
                    result with { Value = malformed }),
                }
            };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(program.EntryPoint, changed),
            }));
        }

    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(NativeJit.Execute(Compile(Source, policy), policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ConditionalPersistsOnlyInFeatureSelectedProducerAndServicesBothConsumers(NativeRuntimePolicy policy)
    {
        const string Library = "public int32 Choose(bool first) { return first ? 42 : 1; }";
        const string Consumer = "int32 Main() { return Choose(true); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Library), (BoundRuntimePolicy)policy);
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = library.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Choose"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var forgedOldReader = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forgedOldReader.AsSpan(4, 4), 40);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(forgedOldReader));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer), reference,
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = consumerCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)), Is.EqualTo(76));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Choose"], "Conditional", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = nativeConsumerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
    }
}
