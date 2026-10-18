// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
    [TestCase("int32 Stop(bool value) { require false; }")]
    public void RejectsUnprovedOrNonVoidDeclarations(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("[DoesNotReturn] " + source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3018"),Is.True);
    }

    [Test]
    public void ConstructorsCannotPublishANoReturnValueContract()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct S { public int32 Value; [DoesNotReturn] public S() { Value = 0; require false; } }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3018"),Is.True);
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
