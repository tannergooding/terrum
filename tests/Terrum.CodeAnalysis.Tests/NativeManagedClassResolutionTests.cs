// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedClassResolutionTests
{
    private const string ClassA = """
        namespace A;
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        """;

    private const string ClassB = """
        namespace B;
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        """;

    private const string StructB = """
        namespace B;
        struct Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        """;

    [Theory]
    [InlineData("using A; using B;")]
    [InlineData("using B; using A;")]
    public void RejectsAmbiguousImportedClassTypesAndFactoriesRegardlessOfUsingOrder(string imports)
    {
        var compilation = Compile(ClassB, imports + """
            bool Keep(Leaf leaf) { return true; }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                return 0;
            }
            """);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011"
            && diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsClassVersusStructAndStructVersusStructShortNameAmbiguity()
    {
        var compilation = Compile(StructB, """
            using A; using B;
            int32 Main() {
                Leaf value = new B.Leaf(42);
                return value.Value;
            }
            """);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal));

        compilation = Compilation.Create([
            SyntaxTree.Parse("namespace A; struct Leaf { public int32 Value; public Leaf(int32 value) { Value = value; } }"),
            SyntaxTree.Parse(StructB),
            SyntaxTree.Parse("using A; using B; int32 Main() { Leaf value = new Leaf(42); return 0; }"),
        ]);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using A; using B;")]
    [InlineData("using B; using A;")]
    public void ExactQualifiedClassResolvesIndependentlyOfImports(string imports)
    {
        var compilation = Compile(ClassB, imports + """
            bool Keep(A.Leaf leaf) { return leaf.Value == 42; }
            int32 Main() {
                require A.Leaf.TryCreate(42, out var leaf);
                require Keep(leaf);
                return leaf.Value;
            }
            """);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Fact]
    public void ExactClassAndStructNamesRemainDistinct()
    {
        var compilation = Compile(StructB, """
            using A; using B;
            int32 Main() {
                require A.Leaf.TryCreate(42, out var leaf);
                B.Leaf value = new B.Leaf(1);
                return leaf.Value + value.Value;
            }
            """);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Fact]
    public void SameShortClassNameDoesNotGrantPrivateMethodAccess()
    {
        var compilation = Compilation.Create([
            SyntaxTree.Parse("""
                namespace A;
                class Leaf {
                    public int32 Value;
                    public Leaf(int32 value) { Value = value; }
                    private int32 Hidden() { return Value; }
                }
                """),
            SyntaxTree.Parse("""
                namespace B;
                class Leaf {
                    public int32 Value;
                    public Leaf(int32 value) { Value = value; }
                    public int32 ReadOther(A.Leaf other) { return other.Hidden(); }
                }
                int32 Main() { return 0; }
                """),
        ]);
        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011"
            && diagnostic.Message.Contains("private", StringComparison.Ordinal));
    }

    private static Compilation Compile(string otherDeclaration, string consumer) =>
        Compilation.Create([
            SyntaxTree.Parse(ClassA),
            SyntaxTree.Parse(otherDeclaration),
            SyntaxTree.Parse(consumer),
        ]);
}
