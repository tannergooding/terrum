// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class NoReturnTests
{
    [TestCase("require false;")]
    [TestCase("while (true) { }")]
    [TestCase("for (;;) { }")]
    [TestCase("if (false) { return; } require false;")]
    [TestCase("require false; return;")]
    [TestCase("return Stop(value);")]
    [TestCase("while (false) { return; } for (;;) { }")]
    [TestCase("if (value) { require false; } else { for (;;) { } }")]
    [TestCase("if (value) { Stop(false); } else { require false; }")]
    public void ProvesAbsenceOfReachableNormalCompletion(string body)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] void Stop(bool value) { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(program.Functions[0].DoesNotReturn, Is.True);
        Assert.That(program.Functions[0].IsNoThrow, Is.False);
        SemanticVerifier.VerifyLibrary(program);
    }
    [TestCase("void Stop(bool value) { }")]
    [TestCase("void Stop(bool value) { return; }")]
    [TestCase("void Stop(bool value) { if (value) { return; } require false; }")]
    [TestCase("void Stop(bool value) { while (value) { } }")]
    [TestCase("void Stop(bool value) { while (false) { require false; } }")]
    [TestCase("void Stop(bool value) { for (; value;) { } }")]
    [TestCase("void Stop(bool value) { Other(); } void Other() { require false; }")]
    [TestCase("int32 Stop(bool value) { return 42; }")]
    [TestCase("int32 Stop(bool value) { }")]
    public void RejectsUnprovedDeclarations(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("[DoesNotReturn] " + source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3018"),Is.True);
    }

    [Test]
    public void NonUnitNoReturnKeepsItsDeclaredTypeWithoutPublishingAResult()
    {
        const string source = """
            [DoesNotReturn] int32 Stop(bool value) { require false; }
            int32 Main() { return Stop(true); }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var stop = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Stop");
        Assert.That(stop.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(stop.DoesNotReturn, Is.True);
        SemanticVerifier.VerifyLibrary(program);
        var bodyOnly = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] int32 Stop(bool value) { require false; }"));
        var bodyOnlyProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = bodyOnly.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(bodyOnlyProgram, "nonunit-no-return"));
        var persistedStop = global::System.Linq.Enumerable.Single(loaded.Functions,
            static function => function.Name == "Stop");
        Assert.That(persistedStop.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(persistedStop.DoesNotReturn, Is.True);

        var illTyped = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] int32 Stop() { require false; } int32 Main() { bool value = Stop(); return 0; }"));
        Assert.That(illTyped.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(illTyped.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3018"), Is.False);
        var noBottomConversion = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] void Stop() { require false; } int32 Main() { return Stop(); }"));
        Assert.That(noBottomConversion.HasErrors, Is.True);
    }

    [Test]
    public void InstanceConstructorsCanPublishNoReturnWithoutPublishingAReceiver()
    {
        const string source =
            "struct S { public int32 Value; [DoesNotReturn] public S() { Value = 0; require false; } }";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var constructor = global::System.Linq.Enumerable.Single(program.Functions,
            static function => function.IsConstructor);
        Assert.That(constructor.DoesNotReturn, Is.True);
        Assert.That(constructor.ReturnType, Is.Not.EqualTo(PrimitiveType.Void));
        SemanticVerifier.VerifyLibrary(program);

        var normal = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct S { public int32 Value; [DoesNotReturn] public S() { Value = 0; } }"));
        Assert.That(normal.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(normal.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3018"), Is.True);

        var discarded = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct S { public int32 Value; public S() { Value = 0; } } void Main() { new S(); }"));
        Assert.That(discarded.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(discarded.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3009"), Is.True);
    }

    [Test]
    public void NonReturningCallsPropagateThroughInitializersAndArguments()
    {
        const string source = """
            struct S { public int32 Value; [DoesNotReturn] public S() { Value = 0; require false; } }
            class C { public int32 Value; public C() { Value = 0; } }
            [DoesNotReturn] int32 Construct() { S value = new S(); }
            [DoesNotReturn] int32 Stop() { require false; }
            [DoesNotReturn] int32 StopLater() { require false; }
            [DoesNotReturn] int32 Assign() { mut int32 value = 0; value = Stop(); }
            struct T { public int32 Value; [DoesNotReturn] public T() { Value = Stop(); } }
            void ConsumeS(S value) { }
            [DoesNotReturn] int32 PassConstructor() { ConsumeS(new S()); }
            void Consume(int32 value) { }
            void ConsumePair(int32 value, int32 other) { }
            [DoesNotReturn] int32 ForwardAfterStop() { ConsumePair(Stop(), StopLater()); }
            [DoesNotReturn] int32 Forward() { Consume(Stop()); }
            [DoesNotReturn] int32 ReturnFromStop() { return Stop(); }
            [DoesNotReturn] bool StopCondition() { require false; }
            [DoesNotReturn] int32 BranchOnStop() {
                if (StopCondition()) { return 1; } else { return 2; }
            }
            [DoesNotReturn] C StopClass() { require false; }
            C CoalesceStop() { return null ?? StopClass(); }
            [DoesNotReturn] C? StopOptional() { require false; }
            C CoalesceLeftStop() { return StopOptional() ?? StopClass(); }
            [DoesNotReturn] int32 LoopOnStop() {
                while (StopCondition()) { return 1; }
            }
            [DoesNotReturn] int32 DoWhileStop() {
                do { } while (StopCondition());
            }
            [DoesNotReturn] int32 ForwardConditional(bool choose) {
                Consume(choose ? Stop() : Stop());
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        SemanticVerifier.VerifyLibrary(program);

        var unpublishedOutput = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [DoesNotReturn] void Stop(out int32 value) { value = 42; require false; }
            int32 Main() { Stop(out var value); return value; }
            """));
        Assert.That(unpublishedOutput.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(unpublishedOutput.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3016"), Is.True);

        var shortCircuit = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [DoesNotReturn] bool StopCondition() { require false; }
            [DoesNotReturn] int32 Incorrect() {
                if (false && StopCondition()) { return 1; }
                return 2;
            }
            """));
        Assert.That(shortCircuit.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(shortCircuit.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3018"), Is.True);

        var invalidLaterArgument = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [DoesNotReturn] int32 Stop() { require false; }
            void Consume(int32 first, int32 second) { }
            int32 Main() { Consume(Stop(), false); }
            """));
        Assert.That(invalidLaterArgument.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(invalidLaterArgument.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3018"), Is.False);
    }

    [Test]
    public void NoReturnAndNoThrowAreIndependentBounds()
    {
        var valid = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] [NoThrow] void Spin() { for (;;) { } }"));
        Assert.That(valid.HasErrors, Is.False, string.Join(Environment.NewLine, valid.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = valid.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(program.Functions[0].DoesNotReturn, Is.True);
        Assert.That(program.Functions[0].IsNoThrow, Is.True);
        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] [DoesNotReturn] void Stop() { require false; }"));
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        var duplicate = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] [DoesNotReturn] void Stop() { require false; }"));
        Assert.That(duplicate.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(duplicate.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }
    [TestCase("void Stop() { }")]
    [TestCase("void Stop() { return; }")]
    [TestCase("int32 Stop() { return 42; }")]
    public void PublicIrCannotForgeNormalContinuationGuarantees(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source + " int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.False);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var forged = program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void CallersRequireTheDeclarationRatherThanInspectingTheBody()
    {
        const string Source = "void Stop() { require false; } int32 Main() { Stop(); }";
        var missing = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(missing.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(missing.Diagnostics,static diagnostic => diagnostic.Code == "TRM3012"),Is.True);
        var declared = Compilation.Create(SyntaxTree.Parse("[DoesNotReturn] " + Source));
        Assert.That(declared.HasErrors, Is.False, string.Join(Environment.NewLine, declared.Diagnostics));
        SemanticVerifier.Verify(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = declared.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))());
    }

    [Test]
    public void NonReturningFailureBranchPublishesOnlyTheSuccessfulOutputs()
    {
        const string Source = """
            [DoesNotReturn] void Stop() { require false; }
            bool Try(bool success, out int32 value) {
                if (!success) { return false; }
                value = 42; return true;
            }
            int32 Main() {
                if (!Try(true, out var value)) { Stop(); }
                return value;
            }
            """;
        var valid = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.That(valid.HasErrors, Is.False, string.Join(Environment.NewLine, valid.Diagnostics));
        var invalid = Compilation.Create(SyntaxTree.Parse(Source.Replace("if (!Try", "if (Try", StringComparison.Ordinal)));
        Assert.That(invalid.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase("if (!false) { return 42; }",true)]
    [TestCase("if (true && !false) { return 42; }",true)]
    [TestCase("if (false || true) { return 42; }",true)]
    [TestCase("while (!false) { }",true)]
    [TestCase("require true && false;",true)]
    [TestCase("if (!true) { return 42; }",false)]
    [TestCase("while (!true) { return 42; }",false)]
    [TestCase("require !false;",false)]
    public void BinderAndVerifierUseTheSameBooleanContinuationProof(string body, bool valid)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"));
        Assert.That(compilation.HasErrors, Is.EqualTo(!valid));
        if (valid)
        {
            SemanticVerifier.Verify(((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))());
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3012"),Is.True);
        }
    }
}
