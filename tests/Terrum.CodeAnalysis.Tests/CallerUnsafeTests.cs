// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CallerUnsafeTests
{
    private static BoundProgram CompileLibrary(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Theory]
    [InlineData("unsafe { return Danger(); }")]
    [InlineData("return unsafe(Danger());")]
    [InlineData("unsafe(Danger()); return 42;")]
    public void ExplicitUnsafeScopesAuthorizeOnlyTheirCall(string body)
    {
        var source = $"unsafe int32 Danger() {{ return 42; }} int32 Caller() {{ {body} }}";
        var program = CompileLibrary(source);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Caller")
            .RequiresUnsafeCaller);
    }

    [Fact]
    public void UnsafeDeclarationDoesNotAuthorizeItsBodyOrFollowingCalls()
    {
        var bodyCall = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } unsafe int32 Wrapper() { return Danger(); }"));
        Assert.Null(bodyCall.Program);
        Assert.Contains(bodyCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var leakedScope = Compilation.CreateLibrary(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Caller() { unsafe(Danger()); return Danger(); }"));
        Assert.Null(leakedScope.Program);
        Assert.Contains(leakedScope.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");
    }

    [Fact]
    public void SafeWrapperMayUseUnsafeImplementationWithoutPropagatingTheObligation()
    {
        var program = CompileLibrary("""
            public unsafe int32 Danger() { return 42; }
            public int32 Wrapper() { unsafe { return Danger(); } }
            """);
        Assert.True(Assert.Single(program.Functions, static function => function.Name == "Danger")
            .RequiresUnsafeCaller);
        Assert.False(Assert.Single(program.Functions, static function => function.Name == "Wrapper")
            .RequiresUnsafeCaller);
    }

    [Fact]
    public void UnsafeClassConstructorsRequireAuthorizationThroughTheirFactories()
    {
        const string declaration = """
            class Leaf {
                public int32 Value;
                public unsafe Leaf(int32 value) { Value = value; }
            }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { require Leaf.TryCreate(42, out var leaf); return leaf.Value; }"));
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { unsafe { require Leaf.TryCreate(42, out var leaf); return leaf.Value; } }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
    public void UnsafeGetterRequiresAuthorizationAtThePropertyAccess()
    {
        const string source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { unsafe get { return _value; } }
            }
            """;

        var safeAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); return box.Value; }"));
        Assert.Null(safeAccess.Program);
        Assert.Contains(safeAccess.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedAccess = Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() { require Box.TryCreate(42, out var box); unsafe { return box.Value; } }"));
        Assert.False(authorizedAccess.HasErrors, string.Join(Environment.NewLine, authorizedAccess.Diagnostics));
    }

    [Fact]
    public void GenericSpecializationPreservesCallerUnsafeRequirement()
    {
        const string declaration = """
            unsafe T Identity<T>(T value) where T : [Copy] { return value; }
            """;

        var safeCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return Identity<int32>(42); }"));
        Assert.Null(safeCall.Program);
        Assert.Contains(safeCall.Diagnostics, static diagnostic => diagnostic.Code == "TRM3028");

        var authorizedCall = Compilation.Create(SyntaxTree.Parse(declaration
            + " int32 Main() { return unsafe(Identity<int32>(42)); }"));
        Assert.False(authorizedCall.HasErrors, string.Join(Environment.NewLine, authorizedCall.Diagnostics));
    }

    [Fact]
    public void SemanticModulesRejectCallerUnsafeContracts()
    {
        var program = CompileLibrary("public unsafe int32 Danger() { return 42; }");
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Danger"]));

        var producer = ModuleCompiler.CreateProducer(
            CompileLibrary("public int32 Helper() { return 1; }"), ["Helper"]);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Main() { return unsafe(Danger()); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(compilation.Program), producer));
    }

    [Fact]
    public void TrRmRejectsCallerUnsafeContractsAndPreservesExistingEffects()
    {
        var declaration = CompileLibrary("[NoThrow] public unsafe int32 Danger() { return 42; }");
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(declaration, "unsafe-contract"));

        var safeDeclaration = CompileLibrary("[NoThrow] public int32 Danger() { return 42; }");
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(safeDeclaration, "safe-contract"));
        var function = Assert.Single(loaded.Functions);
        Assert.True(function.IsNoThrow);
        Assert.False(function.RequiresUnsafeCaller);

        var caller = Compilation.Create(SyntaxTree.Parse(
            "unsafe int32 Danger() { return 42; } int32 Main() { return unsafe(Danger()); }"));
        Assert.False(caller.HasErrors, string.Join(Environment.NewLine, caller.Diagnostics));
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Assert.IsType<BoundProgram>(caller.Program), "unsafe-call"));
    }
}
