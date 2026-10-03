// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

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

public sealed class CoalescingTests
{
    [Fact]
    public void LexerAndParserRetainLongestMatchesAndTypeErrors()
    {
        const string source = "int32 Main() { ManagedNode?? invalid = null; return 1; }";
        var invalid = SyntaxTree.Parse(source);
        Assert.Equal(source, invalid.Root.ToFullString());
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "TRM1005");

        const string valid = "int32 Main() { ManagedNode? value = null; value ??= null; return 1; }";
        var lexed = Lexer.Lex(SourceText.From(valid));
        Assert.Equal(valid, SyntaxTree.Parse(lexed).Root.ToFullString());
        Assert.Contains(lexed.Tokens, token => token.Kind == TokenKind.QuestionQuestionEquals);
        var table = ScannerHost.CreateBuffers(SourceText.From(valid))
            .Single(buffer => buffer.Name == "Operators").Values;
        Assert.Contains((int)TokenKind.QuestionQuestionEquals, table);
    }

    [Theory]
    [InlineData("Leaf?? value = null;")]
    [InlineData("Leaf??? value = null;")]
    [InlineData("Leaf? /* gap */ ? value = null;")]
    [InlineData("Namespace.Box<Leaf?>?? value = null;")]
    public void RepeatedOptionalModifiersRetainFollowingStatement(string declaration)
    {
        var source = "int32 Main() { " + declaration + " return 42; }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.Root.ToFullString());
        Assert.True(tree.HasErrors);
        Assert.Equal(1, tree.Root.DescendantNodes().Count(node => node.Kind == SyntaxKind.ReturnStatement));
    }

    [Fact]
    public void CoalescingGroupsBelowOrAndAboveConditionalAndAssociatesRight()
    {
        const string source = """
            int32 Main() {
                ManagedNode? a = null;
                ManagedNode? b = null;
                var result = a ?? b ?? true ? a : b;
                return 1;
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var choice = Assert.Single(tree.Root.DescendantNodes(),
            node => node.Kind == SyntaxKind.ConditionalExpression);
        var outer = Assert.IsType<SyntaxNode>(choice.Children[0]);
        Assert.Equal(SyntaxKind.BinaryExpression, outer.Kind);
        Assert.Equal(SyntaxKind.BinaryExpression, Assert.IsType<SyntaxNode>(outer.Children[2]).Kind);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalValuesAndWritableFieldPlacesEvaluateLazily(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut Leaf? Item;
                public Holder(Leaf? item) { Item = item; }
            }
            Leaf Create(int32 value) {
                require Leaf.TryCreate(value, out var leaf);
                return leaf;
            }
            Leaf Fallback(write Holder holder) {
                holder.Item = Create(40);
                return Create(42);
            }
            int32 Main() {
                require Holder.TryCreate(null, out var holder);
                holder.Item ??= null;
                holder.Item ??= holder.Item;
                Leaf selected = holder.Item ?? Fallback(holder);
                if (selected.Value != 42 || holder.Item is null) { return 1; }
                holder.Item ??= Create(1);
                require holder.Item is some retained;
                require holder.Item is var inferred;
                if (inferred.Value != retained.Value) { return 2; }
                return inferred.Value;
            }
            """;
        Assert.Equal(40, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void CoalescingAssignmentRootsComputedReceiverThroughFallback(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut Leaf? Item;
                public Holder(Leaf? value) { Item = value; }
            }
            write Holder CreateHolder() {
                require Holder.TryCreate(null, out var holder);
                return holder;
            }
            Leaf CreateLeaf() {
                require ManagedHeap.Collect() >= 0;
                require Leaf.TryCreate(42, out var leaf);
                return leaf;
            }
            int32 Main() {
                CreateHolder().Item ??= CreateLeaf();
                return 42;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void KnownCoalescePresencePreservesNestedOutputState(NativeRuntimePolicy policy)
    {
        const string source = """
            [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                value = 42;
                return null;
            }
            [NoThrow] bool Identity(bool input, init ref<int32> result) {
                if (!input) { return false; }
                result = 42;
                return true;
            }
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            int32 Main() {
                require ManagedNode.TryCreate(42, out var leaf);
                init int32 vacant;
                if (Identity((leaf ?? OptionalFallback(init vacant)) is some selected, init var live)) {
                    require Fill(true, init vacant);
                    init int32 nullResult;
                    if (Identity((null ?? OptionalFallback(init nullResult)) is null, init var nullLive)) {
                        return live + vacant + nullLive + nullResult;
                    }
                }
                return 0;
            }
            """;
        Assert.Equal(168, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void KnownCoalescePresenceSkipsInfeasibleBooleanArm(NativeRuntimePolicy policy)
    {
        const string source = """
            [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                value = 42;
                return null;
            }
            [NoThrow] bool Identity(bool input, init ref<int32> result) {
                if (!input) { return false; }
                result = 42;
                return true;
            }
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            int32 Main() {
                require ManagedNode.TryCreate(42, out var leaf);
                init int32 vacant;
                init int32 dead;
                if (Identity(((leaf ?? OptionalFallback(init vacant)) is some selected)
                    || Fill(true, init dead), init var live)) {
                    require Fill(true, init vacant);
                    require Fill(true, init dead);
                    return live + vacant + dead;
                }
                return 0;
            }
            """;
        Assert.Equal(126, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void KnownPresentCoalesceMakesNullArmInfeasible(NativeRuntimePolicy policy)
    {
            const string source = """
                [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                    value = 42;
                    return null;
                }
                [NoThrow] bool Identity(bool input, init ref<int32> result) {
                    if (!input) { return false; }
                    result = 42;
                    return true;
                }
                [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                    if (!publish) { return false; }
                    value = 42;
                    return true;
                }
                int32 Main() {
                    require ManagedNode.TryCreate(42, out var leaf);
                    init int32 vacant;
                    init int32 dead;
                    if (Identity(((leaf ?? OptionalFallback(init vacant)) is null)
                        && Fill(true, init dead), init var live)) {
                        return 1;
                    }
                    require Fill(true, init vacant);
                    require Fill(true, init dead);
                    return vacant + dead;
                }
                """;
            Assert.Equal(84, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void NestedKnownPresentCoalesceKeepsFallbackOutputVacant(NativeRuntimePolicy policy)
    {
        const string source = """
            [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                value = 42;
                return null;
            }
            [NoThrow] bool Fill(init ref<int32> value) {
                value = 42;
                return true;
            }
            int32 Main() {
                require ManagedNode.TryCreate(1, out var leaf);
                init int32 vacant;
                if (((leaf ?? null) ?? OptionalFallback(init vacant)) is some selected) {
                    require Fill(init vacant);
                    return vacant;
                }
                return 0;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ChainedKnownPresentCoalesceKeepsFallbackOutputVacant(NativeRuntimePolicy policy)
    {
            const string source = """
                [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                    value = 42;
                    return null;
                }
                [NoThrow] bool Fill(init ref<int32> value) {
                    value = 42;
                    return true;
                }
                int32 Main() {
                    require ManagedNode.TryCreate(1, out var leaf);
                    init int32 vacant;
                    if ((((leaf ?? null) ?? null) ?? OptionalFallback(init vacant)) is some selected) {
                        require Fill(init vacant);
                        return vacant;
                    }
                    return 0;
                }
                """;
            Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ComputedFieldReceiverRunsOnceAndPresentValueSkipsFallback(NativeRuntimePolicy policy)
    {
        const string source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            mut class Holder {
                public mut Leaf? Item;
                public Holder(Leaf? value) { Item = value; }
            }
            mut class Counter {
                public mut int32 Count;
                public Counter() { Count = 0; }
            }
            write Holder Get(write Counter counter, write Holder holder) {
                counter.Count = counter.Count + 1;
                return holder;
            }
            Leaf Fallback(write Counter counter) {
                counter.Count = counter.Count + 10;
                require Leaf.TryCreate(42, out var leaf);
                return leaf;
            }
            int32 Main() {
                require Counter.TryCreate(out var counter);
                require Holder.TryCreate(null, out var holder);
                Get(counter, holder).Item ??= Fallback(counter);
                Get(counter, holder).Item ??= Fallback(counter);
                require holder.Item is some selected;
                if (selected.Value != 42 || counter.Count != 12) { return 1; }
                return 42;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ConditionalKnownPresentCoalesceSkipsInfeasibleBooleanArm(NativeRuntimePolicy policy)
    {
        const string source = """
            [NoThrow] ManagedNode? OptionalFallback(init ref<int32> value) {
                value = 42;
                return null;
            }
            [NoThrow] bool Fill(init ref<int32> value) {
                value = 42;
                return true;
            }
            int32 Main() {
                require ManagedNode.TryCreate(1, out var leaf);
                bool choose = true;
                init int32 dead;
                if (((choose ? leaf : leaf) ?? OptionalFallback(init dead)) is some selected
                    || Fill(init dead)) {
                    require Fill(init dead);
                    return dead;
                }
                return 0;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void CoalescingPreservesQualifiedWriteCapability(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(1, out var leaf);
                write Leaf? optional = leaf;
                write Leaf retained = optional ?? leaf;
                retained.Value = 42;
                return leaf.Value;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void OptionalFallbacksAndReadonlyTargetPreserveNullabilityAndAccess(NativeRuntimePolicy policy)
    {
        const string source = """
            mut class Leaf { public mut int32 Value; public Leaf(int32 value) { Value = value; } }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                mut Leaf? vacant = null;
                mut Leaf? other = null;
                Leaf requiredFromNull = null ?? leaf;
                var inferredFromNull = null ?? requiredFromNull;
                if (inferredFromNull.Value != 42) { return 5; }
                vacant ??= null;
                vacant ??= other;
                Leaf? fromNull = null ?? other;
                if (!(fromNull is null)) { return 3; }
                var inferred = vacant ?? other;
                if (!(inferred is null)) { return 4; }
                Leaf? stillNull = vacant ?? other ?? null;
                if (!(stillNull is null)) { return 1; }
                other ??= leaf;
                vacant ??= other;
                Leaf? selected = vacant ?? null;
                require selected is some found;
                if (found.Value != 42) { return 2; }
                write Leaf? writable = leaf;
                Leaf? readonlyResult = writable ?? stillNull;
                require readonlyResult is some readonlyLeaf;
                return readonlyLeaf.Value;
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, policy), policy));
    }

    [Fact]
    public void GetterOnlyOptionalResultIsNotAnAssignmentPlace()
    {
        const string source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            class Holder {
                Leaf? _item;
                public Holder(Leaf? item) { _item = item; }
                public Leaf? Item { get { return _item; } }
            }
            int32 Main() {
                require Holder.TryCreate(null, out var holder);
                require Leaf.TryCreate(42, out var leaf);
                holder.Item ??= leaf;
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3008");
    }

    [Theory]
    [InlineData("Leaf? optional = null; optional ??= leaf;")]
    [InlineData("readOnly.Item ??= leaf;")]
    public void CoalescingAssignmentDoesNotStrengthenPlaceAccess(string statement)
    {
        var source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            mut class Holder {
                public mut Leaf? Item;
                public Holder(Leaf? item) { Item = item; }
            }
            void Update(Holder readOnly, Leaf leaf) {
            """
            + statement + """

            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3008");
    }

    [Fact]
    public void FailedFallbackLeavesOptionalFieldNullAndUnwindsStagedReceiverRoot()
    {
        const string source = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            mut class Holder {
                public mut Leaf? Item;
                public Holder(Leaf? value) { Item = value; }
            }
            Leaf Fail() {
                require false;
                require Leaf.TryCreate(1, out var leaf);
                return leaf;
            }
            write Holder CreateHolder() {
                require Holder.TryCreate(null, out var holder);
                return holder;
            }
            int32 Main() {
                require Holder.TryCreate(null, out var holder);
                unsafe {
                    try { holder.Item ??= Fail(); return 1; }
                    catch {
                        if (!(holder.Item is null)) { return 2; }
                        require ManagedHeap.Collect() == 0;
                        try { CreateHolder().Item ??= Fail(); return 3; }
                        catch {
                            require ManagedHeap.Collect() == 1;
                            return 42;
                        }
                    }
                }
            }
            """;
        Assert.Equal(42, NativeJit.Execute(Compile(source, NativeRuntimePolicy.CheckedExceptions),
            NativeRuntimePolicy.CheckedExceptions));
    }

    [Theory]
    [InlineData("mut ManagedNode? value = null; value ??= 42;", "TRM3014")]
    [InlineData("ManagedNode? value = null; init ManagedNode? vacant; vacant ??= value;", "TRM")]
    [InlineData("mut ManagedNode? value = null; value = value ?? 42;", "TRM3014")]
    [InlineData("mut ManagedNode? value = null; write ManagedNode? wrong = value ?? null;", "TRM3007")]
    [InlineData("mut ManagedNode? value = null; ManagedNode? wrong = value ?? true;", "TRM3007")]
    public void RejectsUnsupportedFallbacksAndVacantOptionalPlaces(string statement, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { " + statement + " return 42; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, item => item.Code.StartsWith(diagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void ConstEvalDoesNotTreatManagedCoalescingAsAConstant()
    {
        const string source = """
            int32 Main() {
                ManagedNode? optional = null;
                static require (optional ?? null) is null;
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3027");
    }

    [Fact]
    public void NoThrowChecksPotentiallyActiveFallbackButAllowsTotalOptionalFallback()
    {
        const string safe = """
            [NoThrow] ManagedNode? Select(ManagedNode? optional) {
                return optional ?? null;
            }
            int32 Main() { return 42; }
            """;
        Assert.False(Compilation.Create(SyntaxTree.Parse(safe)).HasErrors);

        const string unsafeSource = """
            ManagedNode? Fail() { require false; return null; }
            [NoThrow] ManagedNode? Select(ManagedNode? optional) {
                return optional ?? Fail();
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(unsafeSource));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public void VerifierRejectsMalformedOptionalAssignmentAndCoalesce()
    {
        var program = Compile("""
            int32 Main() {
                mut ManagedNode? optional = null;
                return 42;
            }
            """, NativeRuntimePolicy.FailFast);
        var function = program.Functions[program.EntryPoint];
        var invalid = new BoundOptionalAssignment(
            new BoundNull(PrimitiveType.OptionalManagedNode, function.Span),
            new BoundNull(PrimitiveType.OptionalManagedNode, function.Span), function.Span);
        var changed = function with { Body = function.Body with {
            Statements = function.Body.Statements.Insert(0, invalid),
        } };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, changed),
        }));
        var validAssignment = new BoundOptionalAssignment(new BoundRead(0, PrimitiveType.OptionalManagedNode,
            function.Span), new BoundNull(PrimitiveType.OptionalManagedNode, function.Span), function.Span);
        var forgedAssignment = validAssignment with {
            Value = new BoundNull(PrimitiveType.OptionalWriteManagedNode, function.Span),
        };
        var invalidAssignment = function with { Body = function.Body with {
            Statements = function.Body.Statements.Insert(1, forgedAssignment),
        } };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, invalidAssignment),
        }));

        var valid = Compile("""
            int32 Main() {
                mut ManagedNode? optional = null;
                ManagedNode? choice = optional ?? null;
                return 42;
            }
            """, NativeRuntimePolicy.FailFast);
        var entry = valid.Functions[valid.EntryPoint];
        var local = Assert.IsType<BoundLocal>(entry.Body.Statements[1]);
        var coalesce = Assert.IsType<BoundCoalesce>(local.Initializer);
        foreach (var forged in new[] {
            coalesce with { Type = PrimitiveType.WriteManagedNode },
            coalesce with { Type = PrimitiveType.OptionalWriteManagedNode,
                Fallback = new BoundNull(PrimitiveType.OptionalWriteManagedNode, coalesce.Span) },
            coalesce with { Fallback = new BoundNull(PrimitiveType.OptionalWriteManagedNode, coalesce.Span) },
        })
        {
            var invalidEntry = entry with { Body = entry.Body with {
                Statements = entry.Body.Statements.SetItem(1, local with { Initializer = forged }),
            } };
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(valid with {
                Functions = valid.Functions.SetItem(valid.EntryPoint, invalidEntry),
            }));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void FeatureVersionRejectsOldHeaderAndLinkedConsumerStillRuns(NativeRuntimePolicy policy)
    {
        const string library = """
            mut class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            bool Fill(bool publish, init ref<Leaf?> output) {
                if (publish) {
                    require Leaf.TryCreate(42, out var leaf);
                    output = leaf;
                    return true;
                }
                return false;
            }
            bool FillNull(init ref<Leaf?> output) {
                output = null;
                return true;
            }
            Leaf? Identity(Leaf? value) { return value; }
            Leaf? NullValue() { return null; }
            public int32 Select() {
                init Leaf? vacant;
                if (vacant is var absentPayload) { return 10; }
                if (vacant is var? vacantValue) { return 1; }
                if (Fill(false, init var value)) { return 2; }
                if (value is var failedPayload) { return 11; }
                if (value is var? failedValue) { return 3; }
                require Fill(true, init value);
                if (!(value is var? nullable)) { return 4; }
                if (nullable is null) { return 5; }
                if (!(value is var present)) { return 6; }
                require FillNull(init var initializedNull);
                if (!(initializedNull is var? capturedNull)) { return 7; }
                if (!(capturedNull is null)) { return 8; }
                if (!(capturedNull is var? repeatedNull)) { return 16; }
                if (!(repeatedNull is null)) { return 17; }
                if (initializedNull is var nonNull) { return 9; }
                if (!(Identity(present) is var? expressionValue)) { return 12; }
                if (!(expressionValue is var expressionPayload)) { return 13; }
                if (expressionPayload.Value != 42) { return 20; }
                if (!(NullValue() is var? expressionNull)) { return 14; }
                if (!(expressionNull is null)) { return 15; }
                if (!(expressionNull is var? repeatedExpressionNull)) { return 18; }
                if (!(repeatedExpressionNull is null)) { return 19; }
                return present.Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(library), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Select"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var old = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(4, 4), 47);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(old));
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Select(); }"),
            reference, runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));
        Assert.Equal(42, NativeJit.Execute(linked, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task IndependentlyCompiledNativeConsumerCallsCoalescingProducerAsync(NativeRuntimePolicy policy)
    {
        const string library = """
            class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            public int32 Select() {
                require Leaf.TryCreate(42, out var value);
                mut Leaf? optional = null;
                optional ??= value;
                return (optional ?? value).Value;
            }
            """;
        var producerProgram = Compilation.CreateLibrary(SyntaxTree.Parse(library),
            (BoundRuntimePolicy)policy);
        Assert.False(producerProgram.HasErrors, string.Join(Environment.NewLine, producerProgram.Diagnostics));
        var producer = NativeModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(producerProgram.Program),
            ["Select"], "Coalescing", policy);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Select(); }"),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), [producer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedGraphCanReplaceCoalescingDependencyAsync(NativeRuntimePolicy policy)
    {
        const string library = """
            mut class Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }
            public int32 Select() {
                require Leaf.TryCreate(42, out var leaf);
                mut Leaf? value = null;
                value ??= leaf;
                return (value ?? leaf).Value;
            }
            """;
        var producer = NativeModuleCompiler.CreateProducer(CompileLibrary(library, policy), ["Select"],
            "Coalescing", policy);
        var consumerProgram = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Select(); }"),
            NativeModuleCompiler.CreateGraphReference([producer]), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(consumerProgram.HasErrors, string.Join(Environment.NewLine, consumerProgram.Diagnostics));
        var consumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(consumerProgram.Program), [producer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(consumer, [producer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var replacement = NativeModuleCompiler.CreateProducer(
            CompileLibrary(library.Replace("TryCreate(42", "TryCreate(43", StringComparison.Ordinal), policy),
            ["Select"], "Coalescing", policy);
        NativeModuleCompiler.ValidateGraph(consumer, [replacement]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(consumer, [replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }
}
