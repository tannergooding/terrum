// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class DiscardTests
{
    [TestCase("int32 Main() { mut int32 _ = 1; return 42; }")]
    [TestCase("int32 Main() { int32 _ = 1; return 42; }")]
    [TestCase("int32 Main() { return _; }")]
    [TestCase("int32 _(int32 value) { return value; } int32 Main() { return 42; }")]
    [TestCase("int32 Value(int32 _) { return 42; } int32 Main() { return 42; }")]
    [TestCase("struct S { public int32 _; } int32 Main() { return 42; }")]
    [TestCase("_ Value() { return (); } int32 Main() { return 42; }")]
    [TestCase("int32 Main() { _ = _; return 42; }")]
    [TestCase("int32 Main() { _ = 1; return 42; }")]
    [TestCase("int32 Main() { _ = Missing(); return 42; }")]
    [TestCase("int32 Main() { _ = int32(1); return _; }")]
    public void ReservedUnderscoreAndInvalidDiscardOperandsDiagnose(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void PrefixedIdentifiersAndPureVoidExpressionRetainTheirMeaning()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 _name = 42;
                _ = ();
                return _name;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("window.Set(0, 42);")]
    [TestCase("_ = window.Set(0, 42);")]
    [TestCase("_ = (window.Set(0, 42));")]
    [TestCase("_ = (((window.Set(0, 42))));")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("_ = window.Set(0);")]
    [TestCase("_ = window.Set(index: 0, value: 42);")]
    [TestCase("_ = window.Set(true, 42);")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("_ = move owner; return owner.Id;")]
    [TestCase("_ = move owner; _ = move owner; return 42;")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3023"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void DiscardDoesNotHideNonconstantCheckedFailureFromNoThrow(BoundRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] void Check(int32 divisor) { _ = int32(42) / divisor; }
            int32 Main() { return 42; }
            """), policy);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var main = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Main");
        var discard = ((global::System.Func<BoundBlock>)(() => { var __nunitActual1 = main.Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual1!; }))();
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = discard.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))();
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual3 = discard.Statements[1]; Assert.That(__nunitActual3, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual3!; }))();
        var drop = ((global::System.Func<BoundDrop>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(cleanup.Finally.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundDrop>()); return (BoundDrop)__nunitActual4!; }))();
        Assert.That(drop.Variable, Is.EqualTo(local.Variable));
        Assert.That(main.Body.Statements.OfType<BoundTryFinally>(), Is.Empty);
    }

    [Test]
    public void DiscardedValuesUseExistingModuleStatementEncodingAndNativeProfile()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 42; }"));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = library.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                _ = Read() + int32(1);
                return 42;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = consumer.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var artifact = ModuleCompiler.CreateConsumer(program, producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        var ordinary = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                int32 unused = Read() + int32(1);
                return 42;
            }
            """), reference);
        Assert.That(ordinary.HasErrors, Is.False, string.Join(Environment.NewLine, ordinary.Diagnostics));
        var oldStyle = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = ordinary.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(artifact.AsSpan(4)), Is.EqualTo(BinaryPrimitives.ReadInt32LittleEndian(oldStyle.AsSpan(4))));
        Assert.That(ModuleCompiler.GetNativeProfileVersion(artifact, isConsumer: true), Is.EqualTo(consumer.NativeProfileVersion));
        Assert.That(global::System.Linq.Enumerable.Any(linked.Functions,static function => function.Name == "Main"),Is.True);
    }

    [Test]
    public void OwningDiscardCleanupSurvivesModuleLinking()
    {
        var library = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 42; }"));
        Assert.That(library.HasErrors, Is.False, string.Join(Environment.NewLine, library.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = library.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), ["Read"]);
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
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var artifact = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = consumer.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), producer);
        var linked = ModuleCompiler.Link(artifact, producer);
        var main = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "Main");
        var discard = ((global::System.Func<BoundBlock>)(() => { var __nunitActual10 = main.Body.Statements[0]; Assert.That(__nunitActual10, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual10!; }))();
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual11 = discard.Statements[1]; Assert.That(__nunitActual11, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual11!; }))();
        _ = ((global::System.Func<BoundDrop>)(() => { var __nunitActual12 = global::System.Linq.Enumerable.Single(cleanup.Finally.Statements); Assert.That(__nunitActual12, Is.TypeOf<BoundDrop>()); return (BoundDrop)__nunitActual12!; }))();
    }
}
