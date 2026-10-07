// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

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

    [Fact]
    public void PrivateSequentialFieldsRetainNaturalOffsetsAndNominalIdentity()
    {
        var program = Compile(Library);
        var inner = program.Structures[0];
        var outer = program.Structures[1];
        Assert.True(inner.IsSequential);
        Assert.True(outer.IsSequential);
        Assert.True(inner.IsInternal);
        Assert.True(outer.IsInternal);
        Assert.All(inner.Fields, field => Assert.True(field.IsPrivate));
        Assert.All(outer.Fields, field => Assert.True(field.IsPrivate));
        Assert.Equal(4, inner.Size);
        Assert.Collection(inner.Fields,
            field => Assert.Equal(0, field.Offset),
            field => Assert.Equal(2, field.Offset));
        Assert.Equal(16, outer.Size);
        Assert.Collection(outer.Fields,
            field => Assert.Equal(0, field.Offset),
            field => Assert.Equal(4, field.Offset),
            field => Assert.Equal(8, field.Offset),
            field => Assert.Equal(12, field.Offset));
    }

    [Fact]
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

        Assert.Equal(16, program.Structures[1].Size);
        Assert.Equal(16, program.Structures[4].Size);
        Assert.Equal(48, program.Classes[0].Size);
        Assert.Collection(program.Classes[0].Fields,
            field => Assert.Equal((0, 16), (field.Offset, field.Size)),
            field => Assert.Equal((16, 16), (field.Offset, field.Size)),
            field => Assert.Equal((32, 16), (field.Offset, field.Size)));
    }

    [Theory]
    [InlineData("[Layout(Sequential)] public struct Bad { int32 Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { public int32 Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { internal int32 Value; }")]
    [InlineData("[Layout(Sequential)] unique struct Bad { int32 Value; }")]
    [InlineData("[Layout(Sequential)] [Layout(Sequential)] struct Bad { int32 Value; }")]
    [InlineData("[Layout(Sequential)] [Layout(Transparent)] struct Bad { int32 Value; }")]
    [InlineData("[Layout(Sequential, 1)] struct Bad { int32 Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { OwnedInt32Buffer Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { Owner Value; } unique struct Owner { OwnedInt32Buffer Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { Bad Value; }")]
    [InlineData("[Layout(Sequential)] struct Bad { int64 A; int64 B; bool C; }")]
    public void UnsupportedOrMalformedSequentialShapesDiagnose(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + "int32 Main() { return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM1001" or "TRM3001" or "TRM3005" or "TRM3023");
    }

    [Fact]
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
            }));
        }
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateLayoutStaysCoupledAndLeavesExistingSemanticWireUnchangedAsync(NativeRuntimePolicy policy)
    {
        var annotated = Compile(Library, policy);
        var automatic = Compile(Library.Replace("[Layout(Sequential)] ", "", StringComparison.Ordinal), policy);
        var producer = ModuleCompiler.CreateProducer(annotated, ["Compute"]);
        var automaticProducer = ModuleCompiler.CreateProducer(automatic, ["Compute"]);
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(automaticProducer.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        Assert.Equal(69,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Empty(reference.Structures);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(37); }"),
            reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, automaticProducer), policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(annotated, ["Compute"], "NodeB", policy);
        var nativeAutomatic = NativeModuleCompiler.CreateProducer(automatic, ["Compute"], "NodeB", policy);
        var nativeReference = NativeModuleCompiler.CreateGraphReference([nativeProducer]);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Compute(37); }"),
            nativeReference, (BoundRuntimePolicy)policy);
        Assert.False(nativeCompilation.HasErrors, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeCompilation.Program), [nativeProducer], policy);
        var snapshot = SHA256.HashData(nativeConsumer);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeAutomatic]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeAutomatic],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(snapshot, SHA256.HashData(nativeConsumer));
    }

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }
}
