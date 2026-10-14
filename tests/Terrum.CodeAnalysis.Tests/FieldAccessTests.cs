// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FieldAccessTests
{
    private const string MutableValues = """
        public struct Inner {
            public mut int32 Value;
            public Inner() { Value = 1; }
        }
        public struct Outer {
            public mut Inner Value;
            public Outer() { Value = new Inner(); }
        }
        """;
    [TestCase("struct S { public int32 Value; public S() { Value = 1; } } int32 Main() { mut S s = new S(); s.Value = 2; return 0; }")]
    [TestCase("struct S { public mut int32 Value; public S() { Value = 1; } } int32 Main() { S s = new S(); s.Value = 2; return 0; }")]
    [TestCase("struct S { public int32 Value; public S() { Value = 1; mut S copy = this; copy.Value = 2; } } int32 Main() { S s = new S(); return 0; }")]
    [TestCase("struct S { public int32 Value; public S() { Value = 1; } } bool Make(out S s) { s = new S(); s.Value = 2; return true; } int32 Main() { return 0; }")]
    public void ReadonlyFieldsAndReadonlyReceiversRejectWrites(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }
    [TestCase("public mut int32 Value;","public int32 Value;")]
    [TestCase("public mut Inner Value;","public Inner Value;")]
    public void EveryFieldInANestedPlaceRequiresWriteAuthority(string mutable, string readOnly)
    {
        var source = MutableValues.Replace(mutable, readOnly, StringComparison.Ordinal)
            + "int32 Main() { mut Outer value = new Outer(); value.Value.Value = 42; return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }

    [Test]
    public void ConstructorAuthorityDoesNotExtendIntoAnotherTypesReadonlyFields()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner { public int32 Value; public Inner() { Value = 1; } }
            struct Outer { public Inner Value; public Outer() { Value = new Inner(); Value.Value = 2; } }
            int32 Main() { Outer value = new Outer(); return 0; }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }

    [Test]
    public void RepeatedMutationModifiersAreRejected()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "struct S { public mut mut int32 Value; public S() { Value = 0; } } int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }
    [TestCase(0)]
    [TestCase(1)]
    public void PublicIrCannotForgeMutationThroughReadonlyFields(int typeIndex)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MutableValues
            + "int32 Main() { mut Outer value = new Outer(); value.Value.Value = 42; return value.Value.Value; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var structure = program.Structures[typeIndex];
        var malformed = program with {
            Structures = program.Structures.SetItem(typeIndex, structure with {
                Fields = structure.Fields.SetItem(0, structure.Fields[0] with { IsMutable = false }),
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(malformed))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(malformed, NativeRuntimePolicy.FailFast))!;
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ImportedFieldAuthorityIsDeclarationDerivedAndReplacementChecked(bool mutable)
    {
        static byte[] Produce(bool writable, int initial)
        {
            var modifier = writable ? "mut " : "";
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                "public struct Cell { public " + modifier + "int32 Value; public Cell(int32 value) { Value = value; } } "
                + "public Cell Make() { return new Cell(" + initial.ToString(System.Globalization.CultureInfo.InvariantCulture) + "); }"));
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            return ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), ["Make"]);
        }
        var producer = Produce(mutable, 1);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Structures[0].Fields[0].IsMutable, Is.EqualTo(mutable));
        var source = "int32 Main() { mut Cell value = Make(); "
            + (mutable ? "value.Value = 42; " : "") + "return value.Value; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), producer);
        SemanticVerifier.Verify(ModuleCompiler.Link(consumer, Produce(mutable, 2)));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer, Produce(!mutable, 2)))!;
        if (!mutable)
        {
            var invalid = Compilation.Create(SyntaxTree.Parse(
                "int32 Main() { mut Cell value = Make(); value.Value = 42; return value.Value; }"), reference);
            Assert.That(invalid.HasErrors, Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(invalid.Diagnostics,static diagnostic => diagnostic.Code == "TRM3008"),Is.True);
        }
    }

    [Test]
    public async Task SourceRangeInvariantsCannotBeBypassedAfterValidatedConstructionAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var range = await File.ReadAllTextAsync(Path.Combine(root, "self-host", "SourceRange.terrum"));
        var entry = "using Terrum.CodeAnalysis; int32 Main() { require SourceRange.TryCreate(0, 1, init var range); "
            + "mut SourceRange copy = range; copy.Start = -1; return 0; }";
        var compilation = Compilation.Create([SyntaxTree.Parse(range), SyntaxTree.Parse(entry)]);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3008"),Is.True);
    }
}
