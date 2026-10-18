// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Storage;

public sealed class NativeManagedClassTests
{
    [TestCase(16,true)]
    [TestCase(17,true)]
    [TestCase(32,true)]
    [TestCase(33,false)]
    public void ManagedClassFieldLimitDiagnosesAtDeclaration(int fieldCount, bool supported)
    {
        var fields = string.Join(" ", Enumerable.Range(0, fieldCount)
            .Select(index => $"public bool F{index};"));
        var assignments = string.Join(" ", Enumerable.Range(0, fieldCount)
            .Select(index => $"F{index} = true;"));
        var source = $"class Wide {{ {fields} public Wide() {{ {assignments} }} }}"
            + " int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (supported)
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("at most 32 fields", StringComparison.Ordinal)),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }

    [Test]
    public void ThirtySecondFieldMustBeInitializedBeforePublication()
    {
        var fields = string.Join(" ", Enumerable.Range(0, 32)
            .Select(index => $"public bool F{index};"));
        var assignments = string.Join(" ", Enumerable.Range(0, 31)
            .Select(index => $"F{index} = true;"));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"class Wide {{ {fields} public Wide() {{ {assignments} }} }}"
            + " int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void ManagedClassPayloadLimitAppliesBelowFieldLimit()
    {
        var fields = string.Join(" ", Enumerable.Range(0, 17)
            .Select(index => $"public int64 F{index};"));
        var assignments = string.Join(" ", Enumerable.Range(0, 17)
            .Select(index => $"F{index} = 0;"));
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"class TooLarge {{ {fields} public TooLarge() {{ {assignments} }} }}"
            + " int32 Main() { return 42; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains("bounded payload", StringComparison.Ordinal)),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("write int32 value = 42;")]
    [TestCase("write Leaf value = leaf;")]
    [TestCase("write Pair value = new Pair(42);")]
    [TestCase("Span<mut Leaf> value = leaf;")]
    public void WriteQualifierDoesNotOpenUnsupportedTypes(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            struct Pair {
                public int32 Value;
                public Pair(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
            """ + declaration + " return leaf.Value; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("mut Leaf @alias = leaf; @alias.Value = 0;")]
    [TestCase("Leaf @alias = leaf; @alias.Value = 0;")]
    [TestCase("Leaf? @alias = leaf; require @alias is some projected; projected.Value = 0;")]
    [TestCase("write Leaf writable = leaf; Leaf view = writable; view.Value = 0;")]
    public void LocalRebindingAndReadonlyProjectionsDoNotUpgradeReferentAccess(string body)
    {
        var source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
            """ + body + """
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("mut class Leaf { public mut int32 Value; public Leaf(int32 value) { Value = value; } } bool Forward(out Leaf leaf) { return Leaf.TryCreate(42, out leaf); }")]
    [TestCase("bool Forward(out ManagedNode node) { return ManagedNode.TryCreate(42, out node); }")]
    public void FactoryOutputsRequireExactWritableStorage(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("""
        mut class Leaf {
            public mut int32 Value;
            public Leaf(int32 value) { Value = value; }
            public mut void Reset() { Value = 0; }
        }
        int32 Main() {
            require Leaf.TryCreate(42, out var writable);
            Leaf view = writable;
            view.Reset();
            return writable.Value;
        }
        """)]
    [TestCase("""
        int32 Main() {
            require ManagedNode.TryCreate(42, out var writable);
            ManagedNode view = writable;
            view.SetValue(0);
            return writable.Value;
        }
        """)]
    public void ReadonlyReceiversCannotCallMutatingMethods(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    private const string Leaf = """
        mut class Leaf {
            public mut int32 Value;
            private int32 Secret;
            public Leaf(int32 value) { Value = value; Secret = value; }
            public int32 Read() { return Value; }
        }
        """;
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void RetainsHeterogeneousClassAndNodeEdges(BoundRuntimePolicy policy)
    {
        var source = """
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            mut class Pair {
                public write Leaf Left;
                public mut Leaf Right;
                public ManagedNode Edge;
                public Pair(write Leaf left, Leaf right, ManagedNode edge) {
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
                write Leaf @alias = pair.Left;
                @alias.Value = 21;
                return pair.Left.Value + pair.Right.Value + pair.Edge.Value - 1;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var pair = global::System.Linq.Enumerable.Single(program.Classes, item => item.Name == "Pair");
        Assert.That(pair.Fields.Count(field => ManagedClassTypes.IsManaged(field.Type)), Is.EqualTo(3));
        Assert.That(pair.Fields[0].IsMutable, Is.False);
        Assert.That(pair.Fields[1].IsMutable, Is.True);
    }
    [TestCase("Leaf value = new Leaf(42); return value.Value;","TRM3005")]
    [TestCase("require Leaf.TryCreate(42, out var leaf); leaf.Secret = 0; return leaf.Value;","TRM3011")]
    public void RejectsNewAndPrivateWrites(string body, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Leaf + "int32 Main() { " + body + " }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void RejectsReadonlyWritesThroughAliasedFixedHandle()
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                Leaf @alias = leaf;
                @alias.Value = 0;
                return leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3008"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3008"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("this.Read(); Value = value;")]
    [TestCase("Leaf @alias = this; Value = value;")]
    public void RejectsPartialThisCallsAndEscapes(string body)
    {
        var source = "class Leaf { public int32 Value; public Leaf(int32 value) { " + body
            + " } public int32 Read() { return Value; } } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var module = ModuleCompiler.CreateProducer(program, ["Proof"]);
        var reference = ModuleCompiler.CreateReference(module);
        Assert.That(reference.Structures, Is.Empty);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).Name, Is.EqualTo("Proof"));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Create"]))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(program, ["Create"],
            (NativeRuntimePolicy)policy))!;
        Assert.That(NativeEmitter.EmitLibrary(program, ["Proof"], (NativeRuntimePolicy)policy), Is.Not.Empty);
    }
    [TestCase("""
        class Leaf {
            public int32 Value;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    [TestCase("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        struct Wrapper { public Leaf Item; }
        int32 Main() { return 42; }
        """)]
    [TestCase("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        int32 Main() { Leaf value = new Leaf(42); return value.Value; }
        """)]
    [TestCase("""
        class Leaf {
            public int32 Value;
            public Leaf(int32 value) { Value = value; }
        }
        class Derived : Leaf {}
        int32 Main() { return 42; }
        """)]
    [TestCase("""
        class Leaf {
            public Leaf? Next;
            public Leaf() {}
        }
        int32 Main() { return 42; }
        """)]
    public void RejectsUnsupportedClassShapesAndIncompleteConstruction(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Null);
    }
}
