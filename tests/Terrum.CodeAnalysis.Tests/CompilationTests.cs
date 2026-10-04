// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CompilationTests
{
    [Theory]
    [InlineData("int32 Main() { return 1 + 2 * 3; }")]
    [InlineData("void Main() { return (); }")]
    [InlineData("int32 Main() { var x = int32(3); return x; }")]
    [InlineData("int32 Main() { int32 x = -2147483648; return x % -1; }")]
    [InlineData("int32 Main() { var x = true; if (x) { return 1; } else { return 0; } }")]
    [InlineData("int32 Main() { if (true) { return 1; } }")]
    [InlineData("int32 Main() { while (true) { } }")]
    [InlineData("int32 Main() { var x = F(4); return x; } int32 F(int32 value) { return value; }")]
    [InlineData("int32 Main() { int32 x = 1; var y = (2 + 3) + x; return y; }")]
    [InlineData("int32 Main() { mut var x = int32(0); while (x < 10) { x = x + 1; } return x; }")]
    [InlineData("void Main() { require Ready(); } bool Ready() { return true; }")]
    [InlineData("struct S { int32 X; } int32 Main() { return 0; }")]
    public void BindsTheExecutableProfile(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        Assert.Same(tree, compilation.SyntaxTree);
        Assert.False(compilation.Diagnostics.IsDefault);
        Assert.False(compilation.SourceDiagnostics.IsDefault);
        Assert.Empty(compilation.SourceDiagnostics);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void CharacterLiteralsRequireAndRespectChar16Context()
    {
        var valid = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                char16 value = '\U00000041';
                if (value == 'A') { return int32(value); }
                return 0;
            }
            """));
        Assert.False(valid.HasErrors, string.Join(Environment.NewLine, valid.Diagnostics));
        var program = Assert.IsType<BoundProgram>(valid.Program);
        SemanticVerifier.Verify(program);
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));

        var missingContext = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { var value = 'A'; return 0; }"));
        Assert.Contains(missingContext.Diagnostics, static diagnostic => diagnostic.Code == "TRM3014");

        var wrongContext = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { int32 value = 'A'; return value; }"));
        Assert.Contains(wrongContext.Diagnostics, static diagnostic => diagnostic.Code == "TRM3014");

        var unrepresentable = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { char16 value = '\\U0001F600'; return 0; }"));
        Assert.Contains(unrepresentable.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }

    [Fact]
    public void ResolvesForwardAliasesAcrossSourceFiles()
    {
        var entry = SyntaxTree.Parse("using Demo; int32 Main() { Count value = 42; return value; }");
        var aliases = SyntaxTree.Parse("namespace Demo; alias Count = Scalar; alias Scalar = int32;");
        var compilation = Compilation.Create([entry, aliases]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Empty(compilation.SourceDiagnostics);
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void RepeatedAliasImportsResolveToOneCandidate()
    {
        var entry = SyntaxTree.Parse("using Demo; using Demo; int32 Main() { Count value = 42; return value; }");
        var aliases = SyntaxTree.Parse("namespace Demo; alias Count = int32;");
        var compilation = Compilation.Create([entry, aliases]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void AliasImportsRemainAmbiguousWhenTheyResolveToDifferentCandidates()
    {
        var entry = SyntaxTree.Parse(
            "using First; using Second; int32 Main() { Count value = 42; return value; }");
        var first = SyntaxTree.Parse("namespace First; alias Count = int32;");
        var second = SyntaxTree.Parse("namespace Second; alias Count = bool;");
        var compilation = Compilation.Create([entry, first, second]);
        Assert.Contains(compilation.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AliasImportsWithTheSameResolvedTypeAreNotAmbiguous()
    {
        var entry = SyntaxTree.Parse(
            "using First; using Second; int32 Main() { Count value = 42; return value; }");
        var first = SyntaxTree.Parse("namespace First; alias Count = int32;");
        var second = SyntaxTree.Parse("namespace Second; alias Count = int32;");
        var compilation = Compilation.Create([entry, first, second]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void PublicAliasesPreserveTheResolvedTypeInExecutableSource()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "public alias Count = int32; int32 Main() { Count value = 42; return value; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void AliasesPreserveNominalTypeIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public struct Number {
                public int32 Value;
                public Number(int32 value) { Value = value; }
            }
            public alias NumberAlias = Number;
            int32 Main() {
                var value = new NumberAlias(42);
                return value.Value;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void AliasesHydrateOptionalManagedTypesInGenericProofs()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Node {
                public int32 Value;
                public Node(int32 value) { Value = value; }
            }
            alias MaybeNode = Node?;
            T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Main() {
                MaybeNode absent = null;
                MaybeNode result = Identity<MaybeNode>(absent);
                if (result is some present) { return present.Value; }
                return 0;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void AliasTargetsRetainManagedWriteCapabilities()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias WritableNode = write ManagedNode; void Set(WritableNode node) { node.SetValue(42); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.VerifyLibrary(compilation.Program);
    }

    [Fact]
    public void GenericAliasesSubstituteAndComposeAcrossSourceFiles()
    {
        var entry = SyntaxTree.Parse("""
            using Demo;
            T Identity<T>(T value) where T : [Copy] {
                Same<T> copy = value;
                return value;
            }
            int32 Main() { return Identity<Scalar<int32>>(42); }
            """);
        var aliases = SyntaxTree.Parse("""
            namespace Demo;
            alias Scalar<T> = T;
            alias Same<T> = T;
            """);
        var compilation = Compilation.Create([entry, aliases]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void GenericAliasesComposeOptionalManagedTargetsAcrossSourceFiles()
    {
        var entry = SyntaxTree.Parse("""
            using Demo;
            int32 Main() {
                Maybe<ManagedNode> absent = null;
                if (absent is some node) { return node.Value; }
                return 0;
            }
            """);
        var aliases = SyntaxTree.Parse("""
            namespace Demo;
            alias Maybe<T> = T?;
            """);
        var compilation = Compilation.Create([entry, aliases]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void GenericAliasesPreserveManagedWriteQualification()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Node { public mut int32 Value; public Node(int32 value) { Value = value; } }
            alias Writable<T> = write T;
            void Set(Writable<Node> node) { node.Value = 42; }
            int32 Main() { return 0; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Fact]
    public void RejectsGenericAliasArityAndUnusedCycles()
    {
        var missing = Compilation.Create(SyntaxTree.Parse(
            "alias Identity<T> = T; int32 Main() { Identity value = 1; return 0; }"));
        Assert.True(missing.HasErrors);
        Assert.Contains(missing.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("requires type arguments", StringComparison.Ordinal));

        var extra = Compilation.Create(SyntaxTree.Parse(
            "alias Identity<T> = T; int32 Main() { Identity<int32, bool> value = 1; return 0; }"));
        Assert.True(extra.HasErrors);
        Assert.Contains(extra.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("expects 1 type arguments but received 2", StringComparison.Ordinal));

        var cycle = Compilation.Create(SyntaxTree.Parse("""
            alias First<T> = Second<T>;
            alias Second<U> = First<U>;
            int32 Main() { return 0; }
            """));
        Assert.True(cycle.HasErrors);
        Assert.Contains(cycle.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("First -> Second -> First", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsRecursiveAliases()
    {
        var recursive = Compilation.Create(SyntaxTree.Parse(
            "alias First = Second; alias Second = First; int32 Main() { return 0; }"));
        Assert.True(recursive.HasErrors);
        Assert.Contains(recursive.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("First -> Second -> First", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsAliasVisibilityAndModuleMetadataBoundariesExplicit()
    {
        var inaccessible = Compilation.Create(SyntaxTree.Parse(
            "struct Hidden { int32 Value; } public alias PublicHidden = Hidden; int32 Main() { return 0; }"));
        Assert.True(inaccessible.HasErrors);
        Assert.Contains(inaccessible.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("non-public target", StringComparison.Ordinal));

        var inaccessibleInstantiation = Compilation.Create(SyntaxTree.Parse("""
            struct Hidden { int32 Value; }
            public alias Identity<T> = T;
            public void Take(Identity<Hidden> value) { }
            int32 Main() { return 0; }
            """));
        Assert.True(inaccessibleInstantiation.HasErrors);
        Assert.Contains(inaccessibleInstantiation.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("non-public target", StringComparison.Ordinal));

        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public alias Count = int32;"));
        Assert.True(library.HasErrors);
        Assert.Contains(library.Diagnostics, static diagnostic =>
            diagnostic.Code == "TRM3001" && diagnostic.Message.Contains("alias metadata", StringComparison.Ordinal));

        var internalLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Count = int32; public Count Get() { return 42; }"));
        Assert.False(internalLibrary.HasErrors, string.Join(Environment.NewLine, internalLibrary.Diagnostics));
        Assert.NotNull(internalLibrary.Program);
    }

    [Theory]
    [InlineData("int32 F() { return 1; }")]
    [InlineData("int32 Main(int32 value) { return value; }")]
    [InlineData("bool Main() { return true; }")]
    [InlineData("int32 Main() { }")]
    [InlineData("int32 Main() { if (false) { return 1; } }")]
    [InlineData("int32 Main() { var value = 1; return value; }")]
    [InlineData("int32 Main() { var value = 1 + 2; return value; }")]
    [InlineData("int32 Main() { return uint32(1); }")]
    [InlineData("int32 Main() { return 2147483648; }")]
    [InlineData("int32 Main() { return -2147483649; }")]
    [InlineData("int32 Main() { return 1.5; }")]
    [InlineData("int32 Main() { return true; }")]
    [InlineData("int32 Main() { if (1) { return 0; } return 1; }")]
    [InlineData("int32 Main() { var x = int32(1); x = 2; return x; }")]
    [InlineData("int32 Main() { mut bool x = true; x = 2; return 0; }")]
    [InlineData("int32 Main() { int32 x = x; return x; }")]
    [InlineData("int32 Main() { return missing; }")]
    [InlineData("int32 Main() { { int32 hidden = 1; } return hidden; }")]
    [InlineData("int32 Main() { int32 x = 1; int32 x = 2; return x; }")]
    [InlineData("int32 Main() { return 1; } int32 Main() { return 2; }")]
    [InlineData("int32 F(int32 a, int32 a) { return a; } int32 Main() { return 0; }")]
    [InlineData("void Main() { return 1; }")]
    [InlineData("int32 Main() { return; }")]
    [InlineData("int32 Main() { return F(); } int32 F(int32 x) { return x; }")]
    [InlineData("int32 Main() { return F(true); } int32 F(int32 x) { return x; }")]
    [InlineData("int32 Main() { return F(out var x); } int32 F(int32 value) { return value; }")]
    [InlineData("int32 Main() { int32? x = 1; return 0; }")]
    [InlineData("int32 Main() { int128 x = 1; return 0; }")]
    [InlineData("int32 Main() { var x = \"text\"; return 0; }")]
    [InlineData("int32 Main() { 1 + 2; return 0; }")]
    [InlineData("int32 Main() { return true + false; }")]
    [InlineData("int32 Main() { if (int32(1) == true) { return 0; } return 1; }")]
    [InlineData("void Main() { require 1; }")]
    [InlineData("void Main() { require int32(1); }")]
    [InlineData("void Main() { require 1 < 2; }")]
    [InlineData("void Main() { require (); }")]
    public void InvalidOrUnsupportedProgramsNeverProduceBoundOutput(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => {
            Assert.InRange(diagnostic.Span.Start, 0, source.Length);
            Assert.InRange(diagnostic.Span.End, diagnostic.Span.Start, source.Length);
        });
        var repeated = Compilation.Create(tree);
        Assert.Equal(compilation.Diagnostics.ToArray(), repeated.Diagnostics.ToArray());
    }

    [Fact]
    public void SyntaxErrorsAndCancellationDoNotBecomeSuccessfulCompilations()
    {
        var tree = SyntaxTree.Parse("int32 Main( { @");
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
        Assert.Equal(tree.Diagnostics, compilation.Diagnostics);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => Compilation.Create(
            SyntaxTree.Parse("void Main() {}"), cancellation.Token));
    }

    [Fact]
    public void LongExpressionChainsDiagnoseRatherThanOverflowTheHostStack()
    {
        var tree = SyntaxTree.Parse("int32 Main() { return " + string.Join(" + ", Enumerable.Repeat("1", 5000)) + "; }");
        Assert.False(tree.HasErrors);
        var compilation = Compilation.Create(tree);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }
}
