// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class InitReferenceTests
{
    internal const string ScalarSource = """
        bool Fill(bool publish, init ref<int32> value) {
            if (*value is init) { return false; }
            if (!publish) { return false; }
            *value = 42;
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
            *value = created;
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
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast,false,false,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true,false,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true,true,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true,false,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false,false,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true,false,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true,true,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true,false,true)]
    public void InfeasibleNestedOutputsRemainVacant(
        NativeRuntimePolicy policy, bool negateInitializationTest, bool compoundInitializationTest,
        bool conditionalInitializationTest)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                *value = 42;
                return true;
            }
            [NoThrow] bool Identity(bool value, init ref<int32> result) {
                if (!value) { return false; }
                *result = 42;
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
        var initializationTest = negateInitializationTest ? "!(ready is init)" : "(ready is init)";
        if (compoundInitializationTest)
        {
            initializationTest = "(" + initializationTest + " && true)";
        }
        if (conditionalInitializationTest)
        {
            initializationTest = "(" + initializationTest + " ? true : false)";
        }
        source.Append("    if (Identity(").Append(initializationTest).Append(" || (");
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(84));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InfeasibleConditionalOutputsRemainVacant(NativeRuntimePolicy policy)
    {
        var source = new StringBuilder("""
            [NoThrow] bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                *value = 42;
                return true;
            }
            [NoThrow] bool Identity(bool value, init ref<int32> result) {
                if (!value) { return false; }
                *result = 42;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(84));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [Test]
    public void ManagedInitReferenceRetainsWritableHandleAndVacantPassingMode()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ManagedSource));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill");
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.True);
        Assert.That(ManagedClassTypes.IsManaged(fill.Variables[1].Type), Is.True);
        var forged = fill with { IsInternal = false, IsPrivate = false };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
            Functions = program.Functions.SetItem(0, forged),
        }))!;
    }
    [TestCase("return node.Value;","TRM3016")]
    [TestCase("require Fill(true, out node); return 42;","TRM3011")]
    [TestCase("require Fill(true, init node); require Fill(true, init node); return 42;","TRM3016")]
    public void ManagedInitCannotReadVacancyOrReusePublishedStorage(string replacement, string diagnostic)
    {
        var source = ManagedSource.Replace("require Fill(true, init node);", replacement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }

    [Test]
    public void InitReferenceHasDistinctBoundParameterAndArgumentModes()
    {
        var tree = SyntaxTree.Parse(ScalarSource);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(ScalarSource));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill");
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.True);
        Assert.That(SyntaxTree.LanguageVersion, Is.EqualTo("syntax-46"));
    }
    [TestCase("if (Fill(false, init var value)) { return 1; }","require Fill(true, init var value);","TRM3016")]
    [TestCase("require Fill(true, init value);","require Fill(true, out value);","TRM3011")]
    [TestCase("require Fill(true, init value);","return value;","TRM3016")]
    [TestCase("require Fill(true, init value);","require Fill(true, init value); require Fill(true, init value);","TRM3016")]
    [TestCase("if (Fill(false, init var value)) { return 1; }","if (Fill(false, init var value)) { return 1; } return value;","TRM3016")]
    public void InitReferencesRejectInvalidSource(string before, string after, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace(before, after,
            StringComparison.Ordinal)));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }

    [Test]
    public void PublicInitContractRetainsItsDistinctModuleMode()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "public bool Fill(", StringComparison.Ordinal).Replace("int32 Main()",
            "public int32 Read()", StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), ["Fill"]);
        Assert.That(global::System.Linq.Enumerable.Single(ModuleCompiler.CreateReference(producer).Functions).Variables[1].IsInit, Is.True);
    }

    [Test]
    public void PrivateInitPreservesExplicitNoThrowProof()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ScalarSource.Replace("bool Fill(",
            "[NoThrow] bool Fill(", StringComparison.Ordinal)));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("int32 value = 1; require Fill(true, init value);")]
    [TestCase("if (Fill(true, init var value)) { require Fill(true, init value); }")]
    [TestCase("if (Fill(true, init var value)) { return value; } return value;")]
    public void AnInitializedOrPossiblyVacantDestinationCannotBeReinitializedOrRead(string body)
    {
        var source = ScalarSource[..ScalarSource.IndexOf("int32 Main()", StringComparison.Ordinal)]
            + "int32 Main() { " + body + " return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void IsInitTracksScalarPublicationAndLifetime(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Fill(bool publish, init ref<int32> value) {
                if (*value is init) { return false; }
                if (!publish) { return false; }
                *value = 42;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(59));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferenceSeparatesDescriptorDestinationAndForwarding(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Write(init ref<int32> destination) {
                if (!(destination is init) || *destination is init) { return false; }
                *destination = 42;
                return true;
            }
            [NoThrow] bool Forward(init ref<int32> destination) {
                if (!(destination is init)) { return false; }
                return Write(init *destination) && *destination is init && *destination == 42;
            }
            int32 Main() {
                init int32 value;
                require Forward(init value);
                return value;
            }
            """), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [Test]
    public void InitDestinationRemainsVacantWhenLaterArgumentEvaluationFails()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] void Fill(init ref<int32> value, int32 ignored) { *value = 42; }
            int32 Fail() { require false; return 0; }
            int32 Main() {
                init int32 value;
                unsafe {
                    try {
                        Fill(init value, Fail());
                    } catch {
                        if (value is init) { return 1; }
                        return 42;
                    }
                }
                return 0;
            }
            """), BoundRuntimePolicy.CheckedExceptions);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }
    [TestCase("1")]
    [TestCase("Get()")]
    public void IsInitRequiresNamedStorage(string operand)
    {
        var source = "int32 Get() { return 42; } int32 Main() { return " + operand
            + " is init ? 1 : 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void IsInitDistinguishesInitializedManagedNullFromVacancy(NativeRuntimePolicy policy)
    {
        const string Source = """
            bool Store(bool publish, init ref<ManagedNode?> value) {
                if (!publish) { return false; }
                *value = null;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(), policy), Is.EqualTo(0));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(), policy), Is.EqualTo(0));
    }

    [Test]
    public void RepeatedInitAliasInOneCallIsRejected()
    {
        const string Source = """
            bool Split(init ref<int32> first, init ref<int32> second) {
                *first = 40;
                *second = 2;
                return true;
            }
            int32 Main() {
                require Split(init var result, init result);
                return result;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code is "TRM3023" or "TRM3016"),Is.True);
    }
    [TestCase("bool Fill(init ref<int32> value) { return true; }","TRM3016")]
    [TestCase("bool Fill(out int32 value) { value = 42; return true; } int32 Main() { require Fill(init var item); return item; }","TRM3011")]
    public void InitModeRequiresNormalReturnPublicationAndMatchingCall(string source, string code)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = Compilation.Create(SyntaxTree.Parse(source.Contains("int32 Main()", StringComparison.Ordinal)
            ? source : source + "int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void InitReferencePublishesOnEveryNonBooleanCompletion(NativeRuntimePolicy policy)
    {
        foreach (var (source, expected) in new (string Source, int Expected)[] {
            ("""
             int32 Fill(init ref<int32> value) { *value = 42; return 7; }
             int32 Main() {
                 init int32 value;
                 int32 status = Fill(init value);
                 return status + value;
             }
             """, 49),
            ("""
             void Fill(init ref<int32> value) { *value = 42; }
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
            Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(expected));
            Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        }
    }
    [TestCase("int32 Fill(init ref<int32> value) { return 42; } int32 Main() { init int32 value; return Fill(init value); }")]
    [TestCase("void Fill(init ref<int32> value) { } int32 Main() { init int32 value; Fill(init value); return 42; }")]
    public void NonBooleanInitReferenceStillRequiresPublicationOnEveryNormalReturn(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3016"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateManagedInitReferencePublishesOnNonBooleanCompletion(NativeRuntimePolicy policy)
    {
        const string source = """
            int32 Create(init ref<write ManagedNode> value) {
                require ManagedNode.TryCreate(42, out var created);
                *value = created;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(49));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateInitHelperServicesUnchangedSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        var sourcePolicy = policy == NativeRuntimePolicy.FailFast
            ? BoundRuntimePolicy.FailFast : BoundRuntimePolicy.CheckedExceptions;
        const string Library = """
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                *value = 42;
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
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producerProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = library.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))();
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var compilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(producer), runtimePolicy: sourcePolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = compilation.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Single(linked.Functions, function => function.Name == "$producer.Fill")
            .Variables[1].IsInit, Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Read"], "NodeB", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), runtimePolicy: sourcePolicy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = nativeCompilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ManagedInitStaysPrivateBehindAnUnchangedScalarConsumerAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            [NoThrow] bool Copy<T>(T value, init ref<T> result) where T : [Copy] {
                *result = value;
                return true;
            }
            [NoThrow] int32 Publish<T>(T value, int32 status, init ref<T> result) where T : [Copy] {
                *result = value;
                return status;
            }
            bool Fill(init ref<write ManagedNode> result) {
                require ManagedNode.TryCreate(41, out var created);
                return Copy(created, init *result);
            }
            int32 Make(init ref<write ManagedNode> result) {
                require ManagedNode.TryCreate(41, out var created);
                return Publish(created, 7, init *result);
            }
            public int32 Read() {
                require Fill(init var node);
                int32 status = Make(init var copy);
                if (status != 7 || copy.Value != node.Value) { return 1; }
                return node.Value + 1;
            }
            """;
        var sourcePolicy = (BoundRuntimePolicy)policy;
        static BoundProgram Compile(string source, BoundRuntimePolicy runtime)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), runtimePolicy: runtime);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            return ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = compilation.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))();
        }
        var producerProgram = Compile(Library, sourcePolicy);
        var updatedProgram = Compile(Library.Replace("TryCreate(41", "TryCreate(42", StringComparison.Ordinal),
            sourcePolicy);
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Read"]);
        var updated = ModuleCompiler.CreateProducer(updatedProgram, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(global::System.Linq.Enumerable.Any(reference.Functions,function => function.Name == "Fill"),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(reference.Functions,function => function.Name == "Make"),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(reference.Functions,function => function.Name.StartsWith("Copy<",
            StringComparison.Ordinal)),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(reference.Functions,function => function.Name.StartsWith("Publish<",
            StringComparison.Ordinal)),Is.False);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            reference, runtimePolicy: sourcePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = consumerCompilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))(),
            producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name.StartsWith("$producer.Copy<",
            StringComparison.Ordinal) && function.Variables[1].IsInit),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,function => function.Name.StartsWith("$producer.Publish<",
            StringComparison.Ordinal) && function.Variables[2].IsInit),Is.True);
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, updated), policy), Is.EqualTo(43));

        var nativeProducer = NativeModuleCompiler.CreateProducer(producerProgram, ["Read"], "NodeB", policy);
        var nativeUpdated = NativeModuleCompiler.CreateProducer(updatedProgram, ["Read"], "NodeB", policy);
        var graphReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var graphCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            graphReference, runtimePolicy: sourcePolicy);
        Assert.That(graphCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = graphCompilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))(), [nativeProducer], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        NativeModuleCompiler.ValidateGraph(graphConsumer, [nativeUpdated]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(graphConsumer, [nativeUpdated],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
    }

    [Test]
    public void OutProducerUsesCurrentSemanticRevision()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(out int32 value) { value = 42; return true; }
            public int32 Read() { require Fill(out var value); return value; }
            """));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual20 = library.Program; Assert.That(__nunitActual20, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual20!; }))(), ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
    }

    [Test]
    public void PrivateInitModeRoundTripsInCurrentSemanticFormat()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Fill(bool publish, init ref<int32> value) {
                if (!publish) { return false; }
                *value = 42;
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
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual21 = library.Program; Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual21!; }))(), ["Read"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        BinaryPrimitives.WriteInt32LittleEndian(producer.AsSpan(4, 4), 48);
        _ = Assert.Throws<ModuleFormatException>(() => _ = ModuleCompiler.CreateReference(producer))!;
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void PrivateGenericInitKeepsManagedOutputRooted(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] bool Copy<T>(T value, init ref<T> result) where T : [Copy] {
                *result = value;
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual22 = compilation.Program; Assert.That(__nunitActual22, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual22!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name.StartsWith("Copy<", StringComparison.Ordinal)
            && function.Variables[1].IsInit),Is.True);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
    }

}
