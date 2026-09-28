// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class DiscardTests
{
    [Theory]
    [InlineData("int32 Main() { mut int32 _ = 1; return 42; }")]
    [InlineData("int32 Main() { int32 _ = 1; return 42; }")]
    [InlineData("int32 Main() { return _; }")]
    [InlineData("int32 _(int32 value) { return value; } int32 Main() { return 42; }")]
    [InlineData("int32 Value(int32 _) { return 42; } int32 Main() { return 42; }")]
    [InlineData("struct S { public int32 _; } int32 Main() { return 42; }")]
    [InlineData("_ Value() { return (); } int32 Main() { return 42; }")]
    [InlineData("int32 Main() { _ = _; return 42; }")]
    [InlineData("int32 Main() { _ = 1; return 42; }")]
    [InlineData("int32 Main() { _ = Missing(); return 42; }")]
    [InlineData("int32 Main() { _ = int32(1); return _; }")]
    public void ReservedUnderscoreAndInvalidDiscardOperandsDiagnose(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void PrefixedIdentifiersAndPureVoidExpressionRetainTheirMeaning()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 _name = 42;
                _ = ();
                return _name;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("window.Set(0, 42);")]
    [InlineData("_ = window.Set(0, 42);")]
    [InlineData("_ = (window.Set(0, 42));")]
    [InlineData("_ = (((window.Set(0, 42))));")]
    public void DiscardAcceptsExistingMutableViewSetStatement(string statement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                require buffer.TryAppend(1);
                mut MutableInt32BufferView window = buffer.SliceMutable(0, 1);
                {{statement}}
                return window.Read(0);
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("_ = window.Set(0);")]
    [InlineData("_ = window.Set(index: 0, value: 42);")]
    [InlineData("_ = window.Set(true, 42);")]
    public void DiscardedMutableViewSetRejectsUnsupportedArguments(string statement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var created);
                mut OwnedInt32Buffer buffer = move created;
                mut MutableInt32BufferView window = buffer.SliceMutable(0, 0);
                {{statement}}
                return 42;
            }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("_ = move owner; return owner.Id;")]
    [InlineData("_ = move owner; _ = move owner; return 42;")]
    public void DiscardedMoveConsumesItsSource(string statement)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            unique struct Owner {
                public int32 Id;
                public Owner(int32 id) { Id = id; }
            }
            int32 Main() {
                Owner owner = new Owner(42);
                {{statement}}
            }
            """));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3023");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void DiscardDoesNotHideNonconstantCheckedFailureFromNoThrow(BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] void Check(int32 divisor) { _ = int32(42) / divisor; }
            int32 Main() { return 42; }
            """), policy);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3017");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void DiscardedUniqueResultGetsImmediateStructuredCleanup()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public OwnedInt32Buffer Buffer;
                public Owner() {
                    require OwnedInt32Buffer.TryCreate(1, out var created);
                    Buffer = move created;
                }
            }
            int32 Main() {
                _ = new Owner();
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var main = Assert.Single(program.Functions, static function => function.Name == "Main");
        var discard = Assert.IsType<BoundBlock>(main.Body.Statements[0]);
        var local = Assert.IsType<BoundLocal>(discard.Statements[0]);
        var cleanup = Assert.IsType<BoundTryFinally>(discard.Statements[1]);
        var drop = Assert.IsType<BoundDrop>(Assert.Single(cleanup.Finally.Statements));
        Assert.Equal(local.Variable, drop.Variable);
        Assert.Empty(main.Body.Statements.OfType<BoundTryFinally>());
    }

    [Fact]
    public void DiscardedValuesUseExistingModuleStatementEncodingAndNativeProfile()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 42; }"));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                _ = Read() + int32(1);
                return 42;
            }
            """), reference);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var program = Assert.IsType<BoundProgram>(consumer.Program);
        var artifact = ModuleCompiler.CreateConsumer(program, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        var ordinary = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 unused = Read() + int32(1);
                return 42;
            }
            """), reference);
        Assert.False(ordinary.HasErrors, string.Join(Environment.NewLine, ordinary.Diagnostics));
        var oldStyle = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(ordinary.Program), producer);
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(oldStyle.AsSpan(4)),
            BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(4)));
        Assert.Equal(consumer.NativeProfileVersion, ModuleCompiler.GetNativeProfileVersion(artifact, isConsumer: true));
        Assert.Contains(linked.Functions, static function => function.Name == "Main");
    }

    [Fact]
    public void OwningDiscardCleanupSurvivesModuleLinking()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 42; }"));
        Assert.False(library.HasErrors, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(library.Program), ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public OwnedInt32Buffer Buffer;
                public Owner() {
                    require OwnedInt32Buffer.TryCreate(1, out var created);
                    Buffer = move created;
                }
            }
            int32 Main() {
                _ = new Owner();
                return 42;
            }
            """), reference);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        var main = Assert.Single(linked.Functions, static function => function.Name == "Main");
        var discard = Assert.IsType<BoundBlock>(main.Body.Statements[0]);
        var cleanup = Assert.IsType<BoundTryFinally>(discard.Statements[1]);
        _ = Assert.IsType<BoundDrop>(Assert.Single(cleanup.Finally.Statements));
    }
}
