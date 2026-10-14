// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class SequentialLayoutTests
{
    internal const string Library = """
        [Layout(Sequential)] struct Inner {
            char16 Unit; bool Valid;
            public Inner(char16 unit) { Unit = unit; Valid = true; }
            public int32 Read() { if (!Valid) { return -1; } return int32(Unit); }
        }
        [Layout(Sequential)] struct Outer {
            bool Present; int32 Number; Inner Value; char16 Tail;
            public Outer(int32 number) {
                Present = true; Number = number; Value = new Inner(char16(2)); Tail = char16(3);
            }
            public int32 Read() {
                if (!Present) { return -1; }
                return Number + Value.Read() + int32(Tail);
            }
        }
        public int32 Compute(int32 number) {
            Outer value = new Outer(number);
            return value.Read();
        }
        """;

    [Test]
    public void PrivateSequentialFieldsRetainNaturalOffsetsAndNominalIdentity()
    {
        var program = Compile(Library);
        var inner = program.Structures[0];
        var outer = program.Structures[1];
        Assert.That(inner.IsSequential, Is.True);
        Assert.That(outer.IsSequential, Is.True);
        Assert.That(inner.IsInternal, Is.True);
        Assert.That(outer.IsInternal, Is.True);
        {var __nunitItems0 = inner.Fields;        Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0.IsPrivate, Is.True); } });}
        {var __nunitItems1 = outer.Fields;        Assert.Multiple(() => { foreach (var __nunitItem1 in __nunitItems1) { Assert.That(__nunitItem1.IsPrivate, Is.True); } });}
        Assert.That(inner.Size, Is.EqualTo(4));
        { var __nunitItems2 = global::System.Linq.Enumerable.ToArray(inner.Fields); Assert.That(__nunitItems2, Has.Length.EqualTo(2)); Assert.That(__nunitItems2[0].Offset, Is.EqualTo(0)); Assert.That(__nunitItems2[1].Offset, Is.EqualTo(2)); }
        Assert.That(outer.Size, Is.EqualTo(16));
        { var __nunitItems3 = global::System.Linq.Enumerable.ToArray(outer.Fields); Assert.That(__nunitItems3, Has.Length.EqualTo(4)); Assert.That(__nunitItems3[0].Offset, Is.EqualTo(0)); Assert.That(__nunitItems3[1].Offset, Is.EqualTo(4)); Assert.That(__nunitItems3[2].Offset, Is.EqualTo(8)); Assert.That(__nunitItems3[3].Offset, Is.EqualTo(12)); }
    }

    [Test]
    public void RepeatedClassFieldsRetainDeepSequentialStructureLayout()
    {
        var program = Compile(Library + """
            [Layout(Sequential)] struct LevelOne {
                Outer Value;
                public LevelOne(int32 number) { Value = new Outer(number); }
            }
            [Layout(Sequential)] struct LevelTwo {
                LevelOne Value;
                public LevelTwo(int32 number) { Value = new LevelOne(number); }
            }
            [Layout(Sequential)] struct LevelThree {
                LevelTwo Value;
                public LevelThree(int32 number) { Value = new LevelTwo(number); }
            }
            [Layout(Sequential)] struct LevelFour {
                LevelThree Value;
                public LevelFour(int32 number) { Value = new LevelThree(number); }
            }
            class Repeated {
                public LevelFour First; public LevelFour Second; public LevelFour Third;
                public Repeated(int32 number) {
                    First = new LevelFour(number); Second = new LevelFour(number); Third = new LevelFour(number);
                }
            }
            """);

        Assert.That(program.Structures[1].Size, Is.EqualTo(16));
        Assert.That(program.Structures[4].Size, Is.EqualTo(16));
        Assert.That(program.Classes[0].Size, Is.EqualTo(48));
        { var __nunitItems4 = global::System.Linq.Enumerable.ToArray(program.Classes[0].Fields); Assert.That(__nunitItems4, Has.Length.EqualTo(3)); Assert.That((__nunitItems4[0].Offset, __nunitItems4[0].Size), Is.EqualTo((0, 16))); Assert.That((__nunitItems4[1].Offset, __nunitItems4[1].Size), Is.EqualTo((16, 16))); Assert.That((__nunitItems4[2].Offset, __nunitItems4[2].Size), Is.EqualTo((32, 16))); }
    }
    [TestCase("[Layout(Sequential)] public struct Bad { int32 Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { public int32 Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { internal int32 Value; }")]
    [TestCase("[Layout(Sequential)] unique struct Bad { int32 Value; }")]
    [TestCase("[Layout(Sequential)] [Layout(Sequential)] struct Bad { int32 Value; }")]
    [TestCase("[Layout(Sequential)] [Layout(Transparent)] struct Bad { int32 Value; }")]
    [TestCase("[Layout(Sequential, 1)] struct Bad { int32 Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { OwnedInt32Buffer Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { Owner Value; } unique struct Owner { OwnedInt32Buffer Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { Bad Value; }")]
    [TestCase("[Layout(Sequential)] struct Bad { int64 A; int64 B; bool C; }")]
    public void UnsupportedOrMalformedSequentialShapesDiagnose(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + "int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code is "TRM1001" or "TRM3001" or "TRM3005" or "TRM3023"),Is.True);
    }

    [Test]
    public void ForgedSequentialIRCannotWidenVisibilityOrMisalignNestedFields()
    {
        var program = Compile(Library);
        var outer = program.Structures[1];
        foreach (var invalid in new[] {
            outer with { IsInternal = false },
            outer with { IsUnique = true },
            outer with { IsTransparent = true },
            outer with { Fields = outer.Fields.SetItem(0, outer.Fields[0] with { IsPrivate = false }) },
            outer with { Fields = outer.Fields.SetItem(2, outer.Fields[2] with { Offset = 9 }) },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program with {
                Structures = program.Structures.SetItem(1, invalid),
            }))!;
        }
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateLayoutStaysCoupledAndLeavesExistingSemanticWireUnchangedAsync(NativeRuntimePolicy policy)
    {
        var annotated = Compile(Library, policy);
        var automatic = Compile(Library.Replace("[Layout(Sequential)] ", "", StringComparison.Ordinal), policy);
        var producer = ModuleCompiler.CreateProducer(annotated, ["Compute"]);
        var automaticProducer = ModuleCompiler.CreateProducer(automatic, ["Compute"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(BinaryPrimitives.ReadInt32LittleEndian(automaticProducer.AsSpan(4, 4))));
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)), Is.EqualTo(76));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(reference.Structures, Is.Empty);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(37); }"),
            reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, automaticProducer), policy), Is.EqualTo(42));

        var nativeProducer = NativeModuleCompiler.CreateProducer(annotated, ["Compute"], "NodeB", policy);
        var nativeAutomatic = NativeModuleCompiler.CreateProducer(automatic, ["Compute"], "NodeB", policy);
        var nativeReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(37); }"),
            nativeReference, (BoundRuntimePolicy)policy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = nativeCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), [nativeProducer], policy);
        var snapshot = SHA256.HashData(nativeConsumer);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeAutomatic]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeAutomatic],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(snapshot));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
    }
}
