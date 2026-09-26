// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedClassTests
{
    private const string Leaf = """
        class Leaf {
            public mut int32 Value;
            private int32 Secret;
            public Leaf(int32 value) { Value = value; Secret = value; }
            public int32 Read() { return Value; }
        }
        """;

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void RetainsHeterogeneousClassAndNodeEdges(BoundRuntimePolicy policy)
    {
        var source = """
            class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Pair {
                public Leaf Left;
                public mut Leaf Right;
                public ManagedNode Edge;
                public Pair(Leaf left, Leaf right, ManagedNode edge) {
                    Left = left;
                    require ManagedHeap.Collect() == 0;
                    Right = right;
                    Edge = edge;
                }
            }
            int32 Main() {
                require Leaf.TryCreate(20, out var left);
                require Leaf.TryCreate(20, out var right);
                require ManagedNode.TryCreate(2, out var edge);
                require Pair.TryCreate(left, right, edge, out var pair);
                Leaf alias = pair.Left;
                alias.Value = 21;
                return pair.Left.Value + pair.Right.Value + pair.Edge.Value - 1;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var pair = Assert.Single(program.Classes, item => item.Name == "Pair");
        Assert.Equal(3, pair.Fields.Count(field => ManagedClassTypes.IsManaged(field.Type)));
        Assert.False(pair.Fields[0].IsMutable);
        Assert.True(pair.Fields[1].IsMutable);
    }

    [Theory]
    [InlineData("Leaf value = new Leaf(42); return value.Value;", "TRM3005")]
    [InlineData("require Leaf.TryCreate(42, out var leaf); leaf.Secret = 0; return leaf.Value;", "TRM3011")]
    public void RejectsNewAndPrivateWrites(string body, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Leaf + "int32 Main() { " + body + " }"));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void RejectsReadonlyWritesThroughAliasedFixedHandle()
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                Leaf alias = leaf;
                alias.Value = 0;
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3008");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void RejectsReadonlyFinalFieldThroughImmutableManagedHandleChain()
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            class Pair {
                public Leaf Left;
                public Pair(Leaf left) { Left = left; }
            }
            int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                require Pair.TryCreate(leaf, out var pair);
                pair.Left.Value = 42;
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3008");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("this.Read(); Value = value;")]
    [InlineData("Leaf alias = this; Value = value;")]
    public void RejectsPartialThisCallsAndEscapes(string body)
    {
        var source = "class Leaf { public int32 Value; public Leaf(int32 value) { " + body
            + " } public int32 Read() { return Value; } } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void ScalarOnlyPrivateClassLibraryPersistsWithoutManagedExports(
        BoundRuntimePolicy policy)
    {
        var source = Leaf + """
            public Leaf Create() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf;
            }
            public int32 Proof() { return Create().Read(); }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var module = ModuleCompiler.CreateProducer(program, ["Proof"]);
        var reference = ModuleCompiler.CreateReference(module);
        Assert.Empty(reference.Structures);
        Assert.Equal("Proof", Assert.Single(reference.Functions).Name);
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Create"]));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, ["Create"],
            (NativeRuntimePolicy)policy));
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Proof"], (NativeRuntimePolicy)policy));
    }

    [Theory]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        struct Wrapper { public Leaf Item; }
        int32 Main() { return 42; }
        """)]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() { Leaf value = new Leaf(42); return value.Value; }
        """)]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        class Derived : Leaf {}
        int32 Main() { return 42; }
        """)]
    [InlineData("""
        class Leaf {
            public Leaf? Next;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    public void RejectsUnsupportedClassShapesAndIncompleteConstruction(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Null(compilation.Program);
    }
}
