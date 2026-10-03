// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class InitReferenceTests
{
    internal const string ScalarSource = """
        bool Fill(bool publish, init ref<int32> value) {
            if (value is init) { return false; }
            if (!publish) { return false; }
            value = 42;
            return true;
        }
        int32 Main() {
            if (Fill(false, init var value)) { return 1; }
            require Fill(true, init value);
            return value;
        }
        """;

    internal const string ManagedSource = """
        bool Fill(bool publish, init ref<write ManagedNode> value) {
            if (!publish) { return false; }
            require ManagedNode.TryCreate(42, out var created);
            value = created;
            return true;
        }
        int32 Main() {
            if (Fill(false, init var node)) { return 1; }
            if (ManagedHeap.Collect() != 0) { return 2; }
            require Fill(true, init node);
            if (ManagedHeap.Collect() != 0) { return 3; }
            return node.Value;
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    public void ConstantConditionalKeepsSelectedOutputPublication(
        NativeRuntimePolicy policy, bool selectTrueArm)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, out int32 value) {
                if (publish) { value = 42; return true; }
                return false;
            }
            int32 Main() {
            """);
        for (var group = 0; group < 6; group++)
        {
            var live = $"live{group}";
            source.Append("    mut int32 ").Append(live).AppendLine(" = 0;");
            var deadCalls = new StringBuilder();
            for (var index = 0; index < 24; index++)
            {
                var dead = $"dead{group}_{index}";
                source.Append("    mut int32 ").Append(dead).AppendLine(" = 0;");
                if (index != 0)
                {
                    deadCalls.Append(" && ");
                }
                deadCalls.Append("Fill(false, out ").Append(dead).Append(')');
            }

            var selected = $"Fill(true, out {live})";
            var deadExpression = deadCalls.ToString();
            source.Append("    if (").Append(selectTrueArm ? "true" : "false").Append(" ? ")
                .Append(selectTrueArm ? selected : deadExpression)
                .Append(" : ").Append(selectTrueArm ? deadExpression : selected)
                .Append(") { return ").Append(live).AppendLine("; }");
        }
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()),
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void ConstantDisjunctionKeepsLeftOutputPublication(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, out int32 value) {
                if (publish) { value = 42; return true; }
                return false;
            }
            int32 Main() {
                mut int32 live = 0;
            """);
        var deadCalls = new StringBuilder("true");
        for (var index = 0; index < 24; index++)
        {
            var dead = $"dead{index}";
            source.Append("    mut int32 ").Append(dead).AppendLine(" = 0;");
            deadCalls.Append(" || Fill(false, out ").Append(dead).Append(')');
        }
        source.Append("    if (Fill(true, out live) && (").Append(deadCalls)
            .AppendLine(")) { return live; }");
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()),
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public void InfeasibleNestedOutputsRemainVacant(NativeRuntimePolicy policy, bool negateInitializationTest)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            [NoThrow] bool Identity(bool value, init ref<int32> result) {
                if (!value) { return false; }
                result = 42;
                return true;
            }
            int32 Main() {
                init int32 ready;
            """);
        if (!negateInitializationTest)
        {
            source.AppendLine("    require Fill(true, init ready);");
        }
        for (var index = 0; index < 24; index++)
        {
            source.Append("    init int32 dead").Append(index).AppendLine(";");
        }
        source.Append("    if (Identity(")
            .Append(negateInitializationTest ? "!(ready is init)" : "(ready is init)")
            .Append(" || (");
        for (var index = 0; index < 24; index++)
        {
            if (index != 0)
            {
                source.Append(" || ");
            }
            source.Append("Fill(false, init dead").Append(index).Append(')');
        }
        source.AppendLine("), init var live)) {");
        source.AppendLine("        require Fill(true, init dead0);");
        source.AppendLine("        return live + dead0;");
        source.AppendLine("    }");
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(84, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void InfeasibleConditionalOutputsRemainVacant(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            [NoThrow] bool Identity(bool value, init ref<int32> result) {
                if (!value) { return false; }
                result = 42;
                return true;
            }
            int32 Main() {
                init int32 ready;
            """);
        source.AppendLine("    require Fill(true, init ready);");
        for (var index = 0; index < 24; index++)
        {
            source.Append("    init int32 dead").Append(index).AppendLine(";");
        }
        source.Append("    if (Identity((ready is init) ? true : (");
        for (var index = 0; index < 24; index++)
        {
            if (index != 0)
            {
                source.Append(" || ");
            }
            source.Append("Fill(false, init dead").Append(index).Append(')');
        }
        source.AppendLine("), init var live)) {");
        source.AppendLine("        require Fill(true, init dead0);");
        source.AppendLine("        return live + dead0;");
        source.AppendLine("    }");
        source.AppendLine("    return 0;");
        source.AppendLine("}");

        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(84, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Fact]
    public void ManagedInitReferenceRetainsWritableHandleAndVacantPassingMode()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ManagedSource));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill");
        Assert.True(fill.Variables[1].IsOut);
        Assert.True(fill.Variables[1].IsInit);
        Assert.True(ManagedClassTypes.IsManaged(fill.Variables[1].Type));
        var forged = fill with { IsInternal = false, IsPrivate = false };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Functions = program.Functions.SetItem(0, forged),
        }));
    }

    [Theory]
    [InlineData("return node.Value;", "TRM3016")]
    [InlineData("require Fill(true, out node); return 42;", "TRM3011")]
    [InlineData("require Fill(true, init node); require Fill(true, init node); return 42;", "TRM3016")]
    public void ManagedInitCannotReadVacancyOrReusePublishedStorage(string replacement, string diagnostic)
    {
        var source = ManagedSource.Replace("require Fill(true, init node);", replacement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
    public void InitReferenceHasDistinctBoundParameterAndArgumentModes()
    {
        var tree = SyntaxTree.Parse(ScalarSource);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(ScalarSource, tree.Root.ToFullString());
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill");
        Assert.True(fill.Variables[1].IsOut);
        Assert.True(fill.Variables[1].IsInit);
        Assert.Equal("syntax-44", SyntaxTree.LanguageVersion);
    }

    [Theory]
    [InlineData("if (Fill(false, init var value)) { return 1; }", "require Fill(true, init var value);", "TRM3016")]
    [InlineData("require Fill(true, init value);", "require Fill(true, out value);", "TRM3011")]
    [InlineData("require Fill(true, init value);", "return value;", "TRM3016")]
    [InlineData("require Fill(true, init value);", "require Fill(true, init value); require Fill(true, init value);", "TRM3016")]
    [InlineData("if (Fill(false, init var value)) { return 1; }", "if (Fill(false, init var value)) { return 1; } return value;", "TRM3016")]
    public void InitReferencesRejectInvalidSource(string before, string after, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace(before, after,
            StringComparison.Ordinal)));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Fact]
    public void PublicInitContractRetainsItsDistinctModuleMode()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "public bool Fill(", StringComparison.Ordinal).Replace("int32 Main()",
            "public int32 Read()", StringComparison.Ordinal)));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), ["Fill"]);
        Assert.True(Assert.Single(ModuleCompiler.CreateReference(producer).Functions).Variables[1].IsInit);
    }

    [Fact]
    public void PrivateInitPreservesExplicitNoThrowProof()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "[NoThrow] bool Fill(", StringComparison.Ordinal)));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("int32 value = 1; require Fill(true, init value);")]
    [InlineData("if (Fill(true, init var value)) { require Fill(true, init value); }")]
    [InlineData("if (Fill(true, init var value)) { return value; } return value;")]
    public void AnInitializedOrPossiblyVacantDestinationCannotBeReinitializedOrRead(string body)
    {
        var source = ScalarSource[..ScalarSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + "int32 Main() { " + body + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void IsInitTracksScalarPublicationAndLifetime(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Fill(bool publish, init ref<int32> value) {
                if (value is init) { return false; }
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            void Write(out int32 value) { value = 17; }
            int32 Main() {
                int32 ready = 7;
                if (!(ready is init)) { return 1; }
                Fill(false, init var value);
                if (value is init) { return 2; }
                require Fill(true, init value);
                if (!(value is init)) { return 3; }
                init int32 local;
                if (local is init) { return 4; }
                local = 42;
                if (!(local is init)) { return 5; }
                local = _;
                if (local is init) { return 6; }
                local = 42;
                if (!(local is init)) { return 7; }
                Write(out var written);
                if (!(written is init)) { return 8; }
                return value + written;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(59, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Get()")]
    public void IsInitRequiresNamedStorage(string operand)
    {
        var source = "int32 Get() { return 42; } int32 Main() { return " + operand
            + " is init ? 1 : 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void IsInitDistinguishesInitializedManagedNullFromVacancy(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Store(bool publish, init ref<ManagedNode?> value) {
                if (!publish) { return false; }
                value = null;
                return true;
            }
            int32 Main() {
                if (Store(false, init var value)) { return 1; }
                if (value is init) { return 2; }
                require Store(true, init value);
                if (!(value is init)) { return 3; }
                if (!(value is null)) { return 4; }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(0, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void IsInitUsesUniqueOwnerLiveness(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(0, out var buffer);
                if (!(buffer is init)) { return 1; }
                buffer = _;
                if (buffer is init) { return 2; }
                return 0;
            }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(0, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
    }

    [Fact]
    public void RepeatedInitAliasInOneCallIsRejected()
    {
        const string Source = """
            bool Split(init ref<int32> first, init ref<int32> second) {
                first = 40;
                second = 2;
                return true;
            }
            int32 Main() {
                require Split(init var result, init result);
                return result;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3023" or "TRM3016");
    }

    [Theory]
    [InlineData("bool Fill(init ref<int32> value) { return true; }", "TRM3016")]
    [InlineData("bool Fill(out int32 value) { value = 42; return true; } int32 Main() { require Fill(init var item); return item; }", "TRM3011")]
    public void InitModeRequiresNormalReturnPublicationAndMatchingCall(string source, string code)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = Compilation.Create(SyntaxTree.Parse(source.Contains("int32 Main()", StringComparison.Ordinal)
            ? source : source + "int32 Main() { return 42; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferencePublishesOnEveryNonBooleanCompletion(NativeRuntimePolicy policy)
    {
        foreach (var (source, expected) in new (string Source, int Expected)[] {
            ("""
             int32 Fill(init ref<int32> value) { value = 42; return 7; }
             int32 Main() {
                 init int32 value;
                 int32 status = Fill(init value);
                 return status + value;
             }
             """, 49),
            ("""
             void Fill(init ref<int32> value) { value = 42; }
             int32 Main() {
                 init int32 value;
                 Fill(init value);
                 return value;
             }
             """, 42),
        })
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source),
                runtimePolicy: (BoundRuntimePolicy)policy);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = Assert.IsType<BoundProgram>(compilation.Program);
            Assert.Equal(expected, NativeJit.Execute(program, policy));
            Assert.NotEmpty(NativeEmitter.Emit(program, policy));
        }
    }

    [Theory]
    [InlineData("int32 Fill(init ref<int32> value) { return 42; } int32 Main() { init int32 value; return Fill(init value); }")]
    [InlineData("void Fill(init ref<int32> value) { } int32 Main() { init int32 value; Fill(init value); return 42; }")]
    public void NonBooleanInitReferenceStillRequiresPublicationOnEveryNormalReturn(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3016");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateManagedInitReferencePublishesOnNonBooleanCompletion(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Create(init ref<write ManagedNode> value) {
                require ManagedNode.TryCreate(42, out var created);
                value = created;
                return 7;
            }
            int32 Main() {
                if (ManagedHeap.Collect() != 0) { return 1; }
                int32 status = Create(init var node);
                if (ManagedHeap.Collect() != 0) { return 2; }
                return status + node.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(49, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateInitHelperServicesUnchangedSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        const string Library = """
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            public int32 Read() {
                if (Fill(false, init var result)) { return 1; }
                require Fill(true, init result);
                return result;
            }
            """;
        const string Consumer = "int32 Main() { return Read(); }";
        var library = Compilation.CreateLibrary(SyntaxTree.Parse(Library), runtimePolicy: sourcePolicy);
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producerProgram = Assert.IsType<BoundProgram>(library.Program);
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Read"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var compilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(producer), runtimePolicy: sourcePolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(Assert.Single(linked.Functions, function => function.Name == "$producer.Fill")
            .Variables[1].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Read"], "NodeB", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: sourcePolicy);
        Assert.False(nativeCompilation.HasErrors, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedInitStaysPrivateBehindAnUnchangedScalarConsumerAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            [NoThrow] bool Copy<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            bool Fill(init ref<write ManagedNode> result) {
                require ManagedNode.TryCreate(41, out var created);
                return Copy(created, init result);
            }
            public int32 Read() {
                require Fill(init var node);
                return node.Value + 1;
            }
            """;
        var sourcePolicy = (BoundRuntimePolicy)policy;
        static BoundProgram Compile(string source, BoundRuntimePolicy runtime)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), runtimePolicy: runtime);
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            return Assert.IsType<BoundProgram>(compilation.Program);
        }
        var producerProgram = Compile(Library, sourcePolicy);
        var updatedProgram = Compile(Library.Replace("TryCreate(41", "TryCreate(42", StringComparison.Ordinal),
            sourcePolicy);
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Read"]);
        var updated = ModuleCompiler.CreateProducer(updatedProgram, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.DoesNotContain(reference.Functions, function => function.Name == "Fill");
        Assert.DoesNotContain(reference.Functions, function => function.Name.StartsWith("Copy<",
            StringComparison.Ordinal));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            reference, runtimePolicy: sourcePolicy);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program),
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Contains(linked.Functions, function => function.Name.StartsWith("$producer.Copy<",
            StringComparison.Ordinal) && function.Variables[1].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.Equal(43, NativeJit.Execute(ModuleCompiler.Link(consumer, updated), policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Read"], "NodeB", policy);
        var nativeUpdated = NativeModuleCompiler.CreateProducer(updatedProgram, ["Read"], "NodeB", policy);
        var graphReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            graphReference, runtimePolicy: sourcePolicy);
        Assert.False(graphCompilation.HasErrors, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(graphCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeUpdated]);
        Assert.Equal(43, await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeUpdated],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void OutProducerUsesCurrentSemanticRevision()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(out int32 value) { value = 42; return true; }
            public int32 Read() { require Fill(out var value); return value; }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
    }

    [Fact]
    public void PrivateInitModeRoundTripsInCurrentSemanticFormat()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                value = 42;
                return true;
            }
            public int32 Read() {
                if (Fill(false, init var result)) { return 1; }
                if (result is init) { return 2; }
                require Fill(true, init result);
                if (!(result is init)) { return 3; }
                return result;
            }
            """));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        BinaryPrimitives.WriteInt32LittleEndian(producer.AsSpan(4, 4), 48);
        _ = Assert.Throws<ModuleFormatException>(() => _ = ModuleCompiler.CreateReference(producer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateGenericInitKeepsManagedOutputRooted(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Copy<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            int32 Main() {
                require ManagedNode.TryCreate(42, out var created);
                require Copy(created, init var node);
                if (!(node is init)) { return 1; }
                if (ManagedHeap.Collect() != 0) { return 2; }
                return node.Value;
            }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Contains(program.Functions, function => function.Name.StartsWith("Copy<", StringComparison.Ordinal)
            && function.Variables[1].IsInit);
        Assert.Equal(42, NativeJit.Execute(program, policy));
    }

}
