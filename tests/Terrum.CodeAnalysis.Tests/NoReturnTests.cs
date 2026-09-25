// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NoReturnTests
{
    [Theory]
    [InlineData("require false;")]
    [InlineData("while (true) { }")]
    [InlineData("for (;;) { }")]
    [InlineData("if (false) { return; } require false;")]
    [InlineData("require false; return;")]
    [InlineData("return Stop(value);")]
    [InlineData("while (false) { return; } for (;;) { }")]
    [InlineData("if (value) { require false; } else { for (;;) { } }")]
    [InlineData("if (value) { Stop(false); } else { require false; }")]
    public void ProvesAbsenceOfReachableNormalCompletion(string body)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] void Stop(bool value) { " + body + " }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.True(program.Functions[0].DoesNotReturn);
        Assert.False(program.Functions[0].IsNoThrow);
        SemanticVerifier.VerifyLibrary(program);
    }

    [Theory]
    [InlineData("void Stop(bool value) { }")]
    [InlineData("void Stop(bool value) { return; }")]
    [InlineData("void Stop(bool value) { if (value) { return; } require false; }")]
    [InlineData("void Stop(bool value) { while (value) { } }")]
    [InlineData("void Stop(bool value) { while (false) { require false; } }")]
    [InlineData("void Stop(bool value) { for (; value;) { } }")]
    [InlineData("void Stop(bool value) { Other(); } void Other() { require false; }")]
    [InlineData("int32 Stop(bool value) { require false; }")]
    public void RejectsUnprovedOrNonVoidDeclarations(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("[DoesNotReturn] " + source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3018");
    }

    [Fact]
    public void ConstructorsCannotPublishANoReturnValueContract()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "struct S { public int32 Value; [DoesNotReturn] public S() { Value = 0; require false; } }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3018");
    }

    [Fact]
    public void NoReturnAndNoThrowAreIndependentBounds()
    {
        var valid = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] [NoThrow] void Spin() { for (;;) { } }"));
        Assert.False(valid.HasErrors, string.Join(Environment.NewLine, valid.Diagnostics));
        var program = Assert.IsType<BoundProgram>(valid.Program);
        Assert.True(program.Functions[0].DoesNotReturn);
        Assert.True(program.Functions[0].IsNoThrow);
        var invalid = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] [DoesNotReturn] void Stop() { require false; }"));
        Assert.True(invalid.HasErrors);
        Assert.Contains(invalid.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
        var duplicate = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[DoesNotReturn] [DoesNotReturn] void Stop() { require false; }"));
        Assert.True(duplicate.HasErrors);
        Assert.Contains(duplicate.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
    }

    [Theory]
    [InlineData("void Stop() { }")]
    [InlineData("void Stop() { return; }")]
    [InlineData("int32 Stop() { return 42; }")]
    public void PublicIrCannotForgeNormalContinuationGuarantees(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source + " int32 Main() { return 0; }"));
        Assert.False(compilation.HasErrors);
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var forged = program with {
            Functions = program.Functions.SetItem(0, program.Functions[0] with { DoesNotReturn = true }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(forged));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(forged, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void CallersRequireTheDeclarationRatherThanInspectingTheBody()
    {
        const string Source = "void Stop() { require false; } int32 Main() { Stop(); }";
        var missing = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.True(missing.HasErrors);
        Assert.Contains(missing.Diagnostics, static diagnostic => diagnostic.Code == "TRM3012");
        var declared = Compilation.Create(SyntaxTree.Parse("[DoesNotReturn] " + Source));
        Assert.False(declared.HasErrors, string.Join(Environment.NewLine, declared.Diagnostics));
        SemanticVerifier.Verify(Assert.IsType<BoundProgram>(declared.Program));
    }

    [Fact]
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
        Assert.False(valid.HasErrors, string.Join(Environment.NewLine, valid.Diagnostics));
        var invalid = Compilation.Create(SyntaxTree.Parse(Source.Replace("if (!Try", "if (Try", StringComparison.Ordinal)));
        Assert.True(invalid.HasErrors);
        Assert.Contains(invalid.Diagnostics, static diagnostic => diagnostic.Code == "TRM3016");
    }

    [Theory]
    [InlineData("if (!false) { return 42; }", true)]
    [InlineData("if (true && !false) { return 42; }", true)]
    [InlineData("if (false || true) { return 42; }", true)]
    [InlineData("while (!false) { }", true)]
    [InlineData("require true && false;", true)]
    [InlineData("if (!true) { return 42; }", false)]
    [InlineData("while (!true) { return 42; }", false)]
    [InlineData("require !false;", false)]
    public void BinderAndVerifierUseTheSameBooleanContinuationProof(string body, bool valid)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " }"));
        Assert.Equal(!valid, compilation.HasErrors);
        if (valid)
        {
            SemanticVerifier.Verify(Assert.IsType<BoundProgram>(compilation.Program));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3012");
        }
    }
}
