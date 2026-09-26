// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedOptionalTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void CyclesAndNullableNodeLocalsBindUnderBothPolicies(BoundRuntimePolicy policy)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repository, "samples", "native",
            "managed-optional-cycle.terrum"));
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal("native-20", compilation.NativeProfileVersion);
        Assert.NotNull(compilation.Program);
    }

    [Theory]
    [InlineData("""
        class Leaf {
            public Leaf? Next;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    [InlineData("""
        class Leaf {
            public Leaf? Next;
            public int32 Value;
            public Leaf() { Next = null; Value = 42; }
        }
        int32 Main() { Leaf? item = null; return item.Value; }
        """)]
    [InlineData("""
        class Leaf {
            public Leaf? Next;
            public Leaf() { Next = null; }
        }
        int32 Main() { int32 invalid = null; return invalid; }
        """)]
    [InlineData("""
        class Leaf {
            public Leaf? Next;
            public Leaf() { this.Read(); Next = null; }
            public int32 Read() { return 42; }
        }
        int32 Main() { return 42; }
        """)]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() {
            require Leaf.TryCreate(42, out var leaf);
            Leaf? optional = leaf;
            Leaf invalid = optional;
            return invalid.Value;
        }
        """)]
    [InlineData("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() { Leaf invalid = null; return invalid.Value; }
        """)]
    [InlineData("""
        bool Reject(out ManagedNode? result) { return false; }
        int32 Main() {
            if (Reject(out var absent)) { return 0; }
            if (absent is null) { return 42; }
            return 0;
        }
        """)]
    [InlineData("""
        bool Reject(out ManagedNode? result) { return false; }
        bool Inspect(ManagedNode? value) { return value is null; }
        int32 Main() {
            if (Reject(out var absent)) { return 0; }
            if (Inspect(absent)) { return 42; }
            return 0;
        }
        """)]
    [InlineData("""
        int32 Main() {
            ManagedNode? optional = null;
            if (optional is some value) { return value.Value; }
            return value.Value;
        }
        """)]
    public void RejectsUntypedOptionalAccessAndIncompleteConstruction(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void OptionalManagedValuesRemainOutsidePersistedModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                ManagedNode? node = null;
                if (node is some present) { return present.Value; }
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Proof"]));
    }
}
