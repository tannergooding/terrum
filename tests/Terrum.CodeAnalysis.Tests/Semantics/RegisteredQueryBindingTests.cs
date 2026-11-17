// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class RegisteredQueryBindingTests
{
    [Test]
    public void TypeIdentityQueriesBindExactEquality()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (typeof(int32) == typeof(int32)) {
                    if (typeof(int32) != typeof(int64)) { return 1; }
                }
                if ((true ? typeof(int32) : typeof(int64))
                    == typeof(int32)) { return 2; }
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void TypeIdentityCannotBeStoredInABoundLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return 0; }"));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The baseline program did not bind.");
        var function = program.Functions[program.EntryPoint];
        var invalidFunction = function with {
            Variables = [
                .. function.Variables,
                new BoundVariable("identity", PrimitiveType.TypeIdentity,
                    false, default),
            ],
        };
        var invalidProgram = program with {
            Functions = program.Functions.SetItem(program.EntryPoint,
                invalidFunction),
        };

        _ = Assert.Throws<ArgumentException>(
            () => SemanticVerifier.Verify(invalidProgram));
    }

    [Test]
    public void OpenGenericTypeIdentityQueriesRetainExactFormalSubstitution()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> {
                T value;
            }

            bool PlaceQuery<T>(T value) {
                return typeof(T) == typeof(T)
                    && typeof(value) == typeof(T);
            }

            bool ConstructedPlaceQuery<T>(Box<T> value) {
                return typeof(value) == typeof(Box<T>);
            }

            bool PointerPlaceQuery<T>(ptr<T> value) {
                return typeof(value) == typeof(ptr<T>);
            }

            bool ReferencePlaceQuery<T>(ref<T> value) {
                return typeof(value) == typeof(ref<T>);
            }

            bool VoidTypeQuery<T>(T value) {
                return typeof(value) != typeof(void);
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
    }

    [Test]
    public void GenericTypeIdentityRejectsConcretePlaceholderFallback()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> {
                T value;
            }

            bool ConstructedPlaceQuery<T>(Box<T> value) {
                return typeof(value) == typeof(Box<T>);
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The generic program did not bind.");
        var definitionOrdinal = program.GenericMethodDefinitions
            .Select((definition, ordinal) => (definition, ordinal))
            .Single(pair => pair.definition.Name.Contains(
                "ConstructedPlaceQuery", StringComparison.Ordinal))
            .ordinal;
        var definition = program.GenericMethodDefinitions[definitionOrdinal];
        var proofProgram = definition.ProofProgram;
        var rewroteQuery = false;
        var proofFunctions = proofProgram.Functions.Select(function =>
            BoundTreeTraversal.RewriteExpressions(function, expression =>
            {
                if (expression is BoundTypeIdentityQuery query
                    && query.Subject.TypeIdentityTerm is {
                        Kind: BoundGenericMethodTypeTermKind.SourceGeneric,
                    })
                {
                    rewroteQuery = true;
                    return query with {
                        Subject = query.Subject with {
                            TypeIdentityTerm =
                                new BoundGenericMethodTypeTerm(
                                    BoundGenericMethodTypeTermKind.Concrete,
                                    query.Subject.Type, -1, -1, [], false,
                                    false, false, false),
                        },
                    };
                }
                return expression;
            })).ToImmutableArray();
        Assert.That(rewroteQuery, Is.True);

        var invalidDefinition = definition with {
            ProofProgram = proofProgram with {
                Functions = proofFunctions,
            },
        };
        var invalidProgram = program with {
            GenericMethodDefinitions =
                program.GenericMethodDefinitions.SetItem(
                    definitionOrdinal, invalidDefinition),
        };

        _ = Assert.Throws<ArgumentException>(
            () => SemanticVerifier.Verify(invalidProgram));
    }

    [Test]
    public void LayoutQueriesBindCompleteTypesAndImmediatePlaceFields()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair {
                public int32 First;
                public int32 Second;
            }

            nint Observe(Pair value) {
                if (typeof(value.First) != typeof(int32)) { return -1; }
                if (unsafe(sizeof(value)) <= 0) { return -2; }
                if (unsafe(alignof(value)) <= 0) { return -3; }
                if (unsafe(offsetof(Pair, Second))
                    != unsafe(offsetof(value.Second))) { return -4; }
                if (alignof(void) != 0) { return -5; }
                if (sizeof(void) != 0) { return -6; }
                return 0;
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void MetadataFieldPathPlaceQueriesDoNotRequireAPlaceRead()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair {
                public int32 First;
                public int32 Second;
            }

            nint Observe(Pair value) {
                return unsafe(offsetof(value.Second));
            }

            int32 Main() { return 0; }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program
            ?? throw new InvalidOperationException("The query program did not bind.");
        var functionIndex = program.Functions.IndexOf(
            program.Functions.Single(static function =>
                function.Name == "Observe"));
        var function = program.Functions[functionIndex];
        var found = false;
        var rewritten = BoundTreeTraversal.RewriteExpressions(function,
            expression =>
            {
                if (expression is BoundLayoutQuery {
                        Kind: BoundLayoutQueryKind.OffsetOfPlace,
                    } query)
                {
                    found = true;
                    return query with {
                        Subject = query.Subject with {
                            Kind = BoundQuerySubjectKind.FieldPathPlace,
                            PlaceExpression = null,
                        },
                    };
                }
                return expression;
            });
        Assert.That(found, Is.True);

        SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(functionIndex, rewritten),
        });
    }

    [Test]
    public void TypeOfDoesNotReadVacantPlaceValues()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Fill(bool publish, ref<init int32> value) {
                if (!publish) { return false; }
                *value = 42;
                return true;
            }

            int32 Main() {
                init int32 value;
                if (Fill(false, ref init value)) { return 1; }
                if (typeof(value) == typeof(int32)) { return 0; }
                return 2;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void NonBlittableLayoutQueriesRequireUnsafe()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair { public int32 Value; }
            int32 Main() { return int32(sizeof(Pair)); }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3028"), Is.True);
    }

    [Test]
    public void CountOfFixedTypesAndKnownShapesBindsNintConstants()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                if (countof([int32; 4]) != 4) { return 1; }
                if (countof([1, 2, 3]) != 3) { return 2; }
                if (countof([1, ..[2, 3]]) != 3) { return 3; }
                if (countof([1; 4]) != 4) { return 4; }
                if (countof("A\U0001F600") != 3) { return 5; }
                if (countof([1; nint.MaxValue]) != nint.MaxValue) {
                    return 6;
                }
                return 0;
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        TestAssertions.NotNull(compilation.Program);
        SemanticVerifier.Verify(compilation.Program);
    }

    [Test]
    public void CountOfRejectsUncountableSpreadsAndUnrepresentableCounts()
    {
        var uncountableSpread = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 2;
                return int32(countof([1, ..value]));
            }
            """));
        Assert.That(uncountableSpread.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);

        var unrepresentable = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                return int32(countof([1; nuint.MaxValue]));
            }
            """));
        Assert.That(unrepresentable.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void CountOfPlaceDoesNotInferACountMemberOrScalarShape()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct CounterLike { public nint Count; }
            int32 Observe(CounterLike value) {
                return int32(countof(value));
            }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(
            static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [Test]
    public void LayoutQueriesRejectCallsInsteadOfInvokingThem()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Value() { return 1; }
            int32 Main() { return int32(sizeof(Value())); }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3007"), Is.True);
    }
}
