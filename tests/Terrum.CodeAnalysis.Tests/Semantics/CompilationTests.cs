// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class CompilationTests
{
    [TestCase("int32 Main() { return 1 + 2 * 3; }")]
    [TestCase("void Main() { return (); }")]
    [TestCase("int32 Main() { var x = int32(3); return x; }")]
    [TestCase("int32 Main() { int32 x = -2147483648; return x % -1; }")]
    [TestCase("int32 Main() { var x = true; if (x) { return 1; } else { return 0; } }")]
    [TestCase("int32 Main() { if (true) { return 1; } }")]
    [TestCase("int32 Main() { while (true) { } }")]
    [TestCase("int32 Main() { var x = F(4); return x; } int32 F(int32 value) { return value; }")]
    [TestCase("int32 Main() { int32 x = 1; var y = (2 + 3) + x; return y; }")]
    [TestCase("int32 Main() { mut var x = int32(0); while (x < 10) { x = x + 1; } return x; }")]
    [TestCase("void Main() { require Ready(); } bool Ready() { return true; }")]
    [TestCase("struct S { int32 X; } int32 Main() { return 0; }")]
    public void BindsTheExecutableProfile(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        Assert.That(compilation.SyntaxTree, Is.SameAs(tree));
        Assert.That(compilation.Diagnostics.IsDefault, Is.False);
        Assert.That(compilation.SourceDiagnostics.IsDefault, Is.False);
        Assert.That(compilation.SourceDiagnostics, Is.Empty);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void CharacterLiteralsRequireAndRespectChar16Context()
    {
        var valid = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                char16 value = '\U00000041';
                if (value == 'A') { return int32(value); }
                return 0;
            }
            """));
        Assert.That(valid.HasErrors, Is.False, string.Join(Environment.NewLine, valid.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = valid.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        SemanticVerifier.Verify(program);
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var missingContext = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { var value = 'A'; return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(missingContext.Diagnostics,static diagnostic => diagnostic.Code == "TRM3014"),Is.True);

        var wrongContext = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { int32 value = 'A'; return value; }"));
        Assert.That(global::System.Linq.Enumerable.Any(wrongContext.Diagnostics,static diagnostic => diagnostic.Code == "TRM3014"),Is.True);

        var unrepresentable = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { char16 value = '\\U0001F600'; return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(unrepresentable.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }

    [Test]
    public void ResolvesForwardAliasesAcrossSourceFiles()
    {
        var entry = SyntaxTree.Parse("using Demo; int32 Main() { Count value = 42; return value; }");
        var aliases = SyntaxTree.Parse("namespace Demo; alias Count = Scalar; alias Scalar = int32;");
        var compilation = Compilation.Create([entry, aliases]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.SourceDiagnostics, Is.Empty);
        TestAssertions.NotNull(compilation.Program);
        Assert.That(compilation.SyntaxTree, Is.SameAs(entry));
        Assert.That(compilation.SyntaxTrees[0], Is.SameAs(entry));
        Assert.That(compilation.SyntaxTrees[1], Is.SameAs(aliases));
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void RepeatedAliasImportsResolveToOneCandidate()
    {
        var entry = SyntaxTree.Parse("using Demo; using Demo; int32 Main() { Count value = 42; return value; }");
        var aliases = SyntaxTree.Parse("namespace Demo; alias Count = int32;");
        var compilation = Compilation.Create([entry, aliases]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void AliasImportsRemainAmbiguousWhenTheyResolveToDifferentCandidates()
    {
        var entry = SyntaxTree.Parse(
            "using First; using Second; int32 Main() { Count value = 42; return value; }");
        var first = SyntaxTree.Parse("namespace First; alias Count = int32;");
        var second = SyntaxTree.Parse("namespace Second; alias Count = bool;");
        var compilation = Compilation.Create([entry, first, second]);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)),Is.True);
    }

    [Test]
    public void AliasImportsWithTheSameResolvedTypeAreNotAmbiguous()
    {
        var entry = SyntaxTree.Parse(
            "using First; using Second; int32 Main() { Count value = 42; return value; }");
        var first = SyntaxTree.Parse("namespace First; alias Count = int32;");
        var second = SyntaxTree.Parse("namespace Second; alias Count = int32;");
        var compilation = Compilation.Create([entry, first, second]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void PublicAliasesPreserveTheResolvedTypeInExecutableSource()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "public alias Count = int32; int32 Main() { Count value = 42; return value; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }
    [TestCase("private alias Count = int32;")]
    [TestCase("[NoThrow] alias Count = int32;")]
    public void RejectsUnsupportedAliasDeclarationModifiers(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            declaration + " int32 Main() { return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }

    [Test]
    public void AliasesPreserveNominalTypeIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void AliasTargetsRetainManagedWriteCapabilities()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias WritableNode = write ManagedNode; void Set(WritableNode node) { node.SetValue(42); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.VerifyLibrary(compilation.Program);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void GenericAliasesPreserveManagedWriteQualification()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Node { public mut int32 Value; public Node(int32 value) { Value = value; } }
            alias Writable<T> = write T;
            void Set(Writable<Node> node) { node.Value = 42; }
            int32 Main() { return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void RejectsGenericAliasArityAndUnusedCycles()
    {
        var missing = Compilation.Create(SyntaxTree.Parse(
            "alias Identity<T> = T; int32 Main() { Identity value = 1; return 0; }"));
        Assert.That(missing.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(missing.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("requires type arguments", StringComparison.Ordinal)),Is.True);

        var extra = Compilation.Create(SyntaxTree.Parse(
            "alias Identity<T> = T; int32 Main() { Identity<int32, bool> value = 1; return 0; }"));
        Assert.That(extra.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(extra.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("expects 1 type arguments but received 2", StringComparison.Ordinal)),Is.True);

        var duplicateParameters = Compilation.Create(SyntaxTree.Parse(
            "alias Identity<T, T> = T; int32 Main() { return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(duplicateParameters.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3001" && diagnostic.Message.Contains("repeat a type parameter", StringComparison.Ordinal)),Is.True);

        var cycle = Compilation.Create(SyntaxTree.Parse("""
            alias First<T> = Second<T>;
            alias Second<U> = First<U>;
            int32 Main() { return 0; }
            """));
        Assert.That(cycle.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(cycle.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("First -> Second -> First", StringComparison.Ordinal)),Is.True);
    }

    [Test]
    public void RejectsRecursiveAliases()
    {
        var recursive = Compilation.Create(SyntaxTree.Parse(
            "alias First = Second; alias Second = First; int32 Main() { return 0; }"));
        Assert.That(recursive.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(recursive.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("First -> Second -> First", StringComparison.Ordinal)),Is.True);
    }

    [Test]
    public void KeepsAliasVisibilityAndModuleMetadataBoundariesExplicit()
    {
        var inaccessible = Compilation.Create(SyntaxTree.Parse(
            "struct Hidden { int32 Value; } public alias PublicHidden = Hidden; int32 Main() { return 0; }"));
        Assert.That(inaccessible.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(inaccessible.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("non-public target", StringComparison.Ordinal)),Is.True);

        var inaccessibleClass = Compilation.Create(SyntaxTree.Parse(
            "class Hidden { public int32 Value; public Hidden(int32 value) { Value = value; } } "
                + "public alias PublicHidden = Hidden; int32 Main() { return 0; }"));
        Assert.That(inaccessibleClass.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(inaccessibleClass.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("non-public target", StringComparison.Ordinal)),Is.True);

        var publicClass = Compilation.Create(SyntaxTree.Parse(
            "public class Visible { public int32 Value; public Visible(int32 value) { Value = value; } } "
                + "public alias PublicVisible = Visible; int32 Main() { return 0; }"));
        Assert.That(publicClass.HasErrors, Is.False, string.Join(Environment.NewLine, publicClass.Diagnostics));

        var inaccessibleInstantiation = Compilation.Create(SyntaxTree.Parse("""
            struct Hidden { int32 Value; }
            public alias Identity<T> = T;
            public void Take(Identity<Hidden> value) { }
            int32 Main() { return 0; }
            """));
        Assert.That(inaccessibleInstantiation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(inaccessibleInstantiation.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3005" && diagnostic.Message.Contains("non-public target", StringComparison.Ordinal)),Is.True);

        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public alias Count = int32;"));
        Assert.That(library.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(library.Diagnostics,static diagnostic =>
            diagnostic.Code == "TRM3001" && diagnostic.Message.Contains("alias metadata", StringComparison.Ordinal)),Is.True);

        var internalLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(
            "alias Count = int32; public Count Get() { return 42; }"));
        Assert.That(internalLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, internalLibrary.Diagnostics));
        TestAssertions.NotNull(internalLibrary.Program);
    }
    [TestCase("int32 F() { return 1; }")]
    [TestCase("int32 Main(int32 value) { return value; }")]
    [TestCase("bool Main() { return true; }")]
    [TestCase("int32 Main() { }")]
    [TestCase("int32 Main() { if (false) { return 1; } }")]
    [TestCase("int32 Main() { var value = 1; return value; }")]
    [TestCase("int32 Main() { var value = 1 + 2; return value; }")]
    [TestCase("int32 Main() { return uint32(1); }")]
    [TestCase("int32 Main() { return 2147483648; }")]
    [TestCase("int32 Main() { return -2147483649; }")]
    [TestCase("int32 Main() { return 1.5; }")]
    [TestCase("int32 Main() { return true; }")]
    [TestCase("int32 Main() { if (1) { return 0; } return 1; }")]
    [TestCase("int32 Main() { var x = int32(1); x = 2; return x; }")]
    [TestCase("int32 Main() { mut bool x = true; x = 2; return 0; }")]
    [TestCase("int32 Main() { int32 x = x; return x; }")]
    [TestCase("int32 Main() { return missing; }")]
    [TestCase("int32 Main() { { int32 hidden = 1; } return hidden; }")]
    [TestCase("int32 Main() { int32 x = 1; int32 x = 2; return x; }")]
    [TestCase("int32 Main() { return 1; } int32 Main() { return 2; }")]
    [TestCase("int32 F(int32 a, int32 a) { return a; } int32 Main() { return 0; }")]
    [TestCase("void Main() { return 1; }")]
    [TestCase("int32 Main() { return; }")]
    [TestCase("int32 Main() { return F(); } int32 F(int32 x) { return x; }")]
    [TestCase("int32 Main() { return F(true); } int32 F(int32 x) { return x; }")]
    [TestCase("int32 Main() { return F(out var x); } int32 F(int32 value) { return value; }")]
    [TestCase("int32 Main() { int32? x = 1; return 0; }")]
    [TestCase("int32 Main() { int128 x = 1; return 0; }")]
    [TestCase("int32 Main() { var x = \"text\"; return 0; }")]
    [TestCase("int32 Main() { 1 + 2; return 0; }")]
    [TestCase("int32 Main() { return true + false; }")]
    [TestCase("int32 Main() { if (int32(1) == true) { return 0; } return 1; }")]
    [TestCase("void Main() { require 1; }")]
    [TestCase("void Main() { require int32(1); }")]
    [TestCase("void Main() { require 1 < 2; }")]
    [TestCase("void Main() { require (); }")]
    public void InvalidOrUnsupportedProgramsNeverProduceBoundOutput(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
        {var __nunitItems0 = compilation.Diagnostics;        Assert.Multiple(() => {
            foreach (var __nunitItem1 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem1.Span.Start, Is.InRange(0, source.Length));
                    Assert.That(__nunitItem1.Span.End, Is.InRange(__nunitItem1.Span.Start, source.Length));
                }
            }
        });}
        var repeated = Compilation.Create(tree);
        Assert.That(repeated.Diagnostics.ToArray(), Is.EqualTo(compilation.Diagnostics.ToArray()));
    }

    [Test]
    public void SyntaxErrorsAndCancellationDoNotBecomeSuccessfulCompilations()
    {
        var tree = SyntaxTree.Parse("int32 Main( { @");
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.EqualTo(tree.Diagnostics));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = Assert.Throws<OperationCanceledException>(() => Compilation.Create(
            SyntaxTree.Parse("void Main() {}"), cancellation.Token))!;
    }

    [Test]
    public void LongExpressionChainsDiagnoseRatherThanOverflowTheHostStack()
    {
        var tree = SyntaxTree.Parse("int32 Main() { return " + string.Join(" + ", Enumerable.Repeat("1", 5000)) + "; }");
        Assert.That(tree.HasErrors, Is.False);
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
}
