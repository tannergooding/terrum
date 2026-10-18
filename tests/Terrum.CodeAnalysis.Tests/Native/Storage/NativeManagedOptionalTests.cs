// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeManagedOptionalTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void CyclesAndNullableNodeLocalsBindUnderBothPolicies(BoundRuntimePolicy policy)
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repository,
            "samples", "LanguageFeatures", "ManagedStorage", "managed-optional-cycle.terrum"));
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.NativeProfileVersion, Is.EqualTo("native-20"));
        TestAssertions.NotNull(compilation.Program);
    }
    [TestCase("""
        class Leaf {
            public Leaf? Next;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    [TestCase("""
        class Leaf {
            public Leaf? Next;
            public int32 Value;
            public Leaf() { Next = null; Value = 42; }
        }
        int32 Main() { Leaf? item = null; return item.Value; }
        """)]
    [TestCase("""
        class Leaf {
            public Leaf? Next;
            public Leaf() { Next = null; }
        }
        int32 Main() { int32 invalid = null; return invalid; }
        """)]
    [TestCase("""
        class Leaf {
            public Leaf? Next;
            public Leaf() { this.Read(); Next = null; }
            public int32 Read() { return 42; }
        }
        int32 Main() { return 42; }
        """)]
    [TestCase("""
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
    [TestCase("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() { Leaf invalid = null; return invalid.Value; }
        """)]
    [TestCase("""
        bool Reject(out ManagedNode? result) { return false; }
        int32 Main() {
            if (Reject(out var absent)) { return 0; }
            if (absent is null) { return 42; }
            return 0;
        }
        """)]
    [TestCase("""
        bool Reject(out ManagedNode? result) { return false; }
        bool Inspect(ManagedNode? value) { return value is null; }
        int32 Main() {
            if (Reject(out var absent)) { return 0; }
            if (Inspect(absent)) { return 42; }
            return 0;
        }
        """)]
    [TestCase("""
        int32 Main() {
            ManagedNode? optional = null;
            if (optional is some value) { return value.Value; }
            return value.Value;
        }
        """)]
    public void RejectsUntypedOptionalAccessAndIncompleteConstruction(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void OptionalManagedValuesRemainOutsidePersistedModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Proof() {
                ManagedNode? node = null;
                if (node is some present) { return present.Value; }
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        _ = Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateProducer(program, ["Proof"]))!;
    }
}
