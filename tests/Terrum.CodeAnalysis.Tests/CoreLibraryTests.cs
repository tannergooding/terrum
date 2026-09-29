// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class CoreLibraryTests
{
    private const string Core = """
        public struct int32 {
            private int32 value;
            public static bool TryParse(int32 input, init ref<int32> result) {
                result = input + 1;
                return true;
            }
            public static int32 Identity(int32 input) { return input.value; }
            public bool TryFormat(int32 minimum) { return value > minimum; }
            public int32 ReadBacking() { return this.value; }
        }
        """;

    [Fact]
    public void CorePrimitiveApisRetainTheirIntrinsicTypeAcrossModuleBinding()
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Core)]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Empty(program.Structures);
        var producer = ModuleCompiler.CreateProducer(program,
            ["int32.TryParse", "int32.TryFormat", "int32.Identity", "int32.ReadBacking"]);
        Assert.Equal(37, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)));
        var consumer = Compilation.Create(SyntaxTree.Parse(
            """
            int32 Main() {
                require int32.TryParse(41, init var value);
                int32 copied = int32.Identity(value);
                if (copied.TryFormat(40)) { return copied.ReadBacking(); }
                return 0;
            }
            """),
            ModuleCompiler.CreateReference(producer));
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumer.Program), producer);
        Assert.Equal(37, BinaryPrimitives.ReadInt32LittleEndian(consumerBytes.AsSpan(4)));
        var linked = ModuleCompiler.Link(consumerBytes, producer);
        Assert.Equal(42, NativeJit.Execute(linked, NativeRuntimePolicy.FailFast));

        var oldVersion = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldVersion.AsSpan(4), 36);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldVersion));
        var oldConsumer = (byte[])consumerBytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldConsumer.AsSpan(4), 33);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(oldConsumer, producer));

        var ordinary = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 42; }"));
        Assert.False(ordinary.HasErrors);
        var ordinaryProducer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(ordinary.Program), ["Value"]);
        BinaryPrimitives.WriteInt32LittleEndian(ordinaryProducer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(ordinaryProducer));
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("char16")]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public void ImplementedPrimitivesCanExposeTheirIntrinsicBackingValue(string name)
    {
        var source = $"public struct {name} {{ private {name} value; public {name} Identity() {{ return value; }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)],
            BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Empty(Assert.IsType<BoundProgram>(compilation.Program).Structures);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("char16")]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public void ImplementedPrimitiveApisExecuteAcrossModuleBoundary(string name)
    {
        var source = $"public struct {name} {{ private {name} value; public static {name} Identity({name} input) {{ return input.value; }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            [$"{name}.Identity"]);
        var body = name == "bool" ? "if (bool.Identity(true)) { return 7; } return 0;"
            : $"return int32({name}.Identity({name}(7)));";
        var consumer = Compilation.Create(SyntaxTree.Parse($"int32 Main() {{ {body} }}"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        Assert.Equal(7, NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void DistinctCorePrimitiveDeclarationsBindAcrossSources()
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse("public struct int32 { private int32 value; public int32 Self() { return value; } }"),
            SyntaxTree.Parse("public struct uint32 { private uint32 value; public uint32 Self() { return value; } }"),
        ], BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(2, Assert.IsType<BoundProgram>(compilation.Program).Functions.Length);
    }

    [Fact]
    public void PrimitiveOwnerVerificationDoesNotReinterpretHistoricalNominalLayouts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public struct Cell { public int32 Value; public int32 Read() { return Value; } }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = Assert.Single(program.Functions);
        var nominal = program with
        {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { Name = "int32" }),
            Functions = program.Functions.SetItem(0, function with
            {
                Name = "int32.Read",
                DeclaringStruct = "int32",
            }),
        };
        SemanticVerifier.VerifyLibrary(nominal);
    }

    [Fact]
    public void PrimitiveReceiverCannotBeForgedWritable()
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Core)],
            BoundRuntimePolicy.FailFast);
        Assert.False(compilation.HasErrors);
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var index = program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.Name == "int32.TryFormat"));
        var function = program.Functions[index];
        var forged = program with
        {
            Functions = program.Functions.SetItem(index, function with
            {
                Variables = function.Variables.SetItem(0,
                    function.Variables[0] with { IsMutable = true }),
            }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forged));
    }

    [Theory]
    [InlineData("struct int32 { private int32 value; }")]
    [InlineData("namespace Example; struct int32 { private int32 value; }")]
    [InlineData("class int32 { public int32 value; }")]
    [InlineData("struct bool { private bool value; }")]
    [InlineData("interface int32<T, R> { static abstract R operator +(T left); }")]
    [InlineData("namespace Example; interface bool<T, R> { static abstract R operator +(T left); }")]
    [InlineData("interface Numeric<int32, R> { static abstract R operator +(int32 left); }")]
    [InlineData("int32 Identity<int32>(int32 input) { return input; }")]
    public void OrdinaryPackagesCannotDeclarePrimitiveBindings(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3003");
    }

    [Theory]
    [InlineData("struct int32 { }")]
    [InlineData("struct int32 { private uint32 value; }")]
    [InlineData("struct int32 { private int32 first; private int32 second; }")]
    [InlineData("struct int32 { public int32 value; }")]
    [InlineData("struct int32 { private static int32 value; }")]
    [InlineData("struct int32 { private int32 value = 0; }")]
    [InlineData("struct int32 { private int32 value; public int32(int32 v) { value = v; } }")]
    [InlineData("struct int32 { private int32 value; public static int32 operator +(int32 x, int32 y) { return x; } }")]
    [InlineData("struct int32 { private int32 value; public mut int32 Update() { return value; } }")]
    [InlineData("struct int32 { private int32 value; } struct int32 { private int32 value; }")]
    [InlineData("namespace Example; struct int32 { private int32 value; }")]
    [InlineData("interface int32<T, R> { static abstract R operator +(T left); }")]
    public void CorePrimitiveShapesAreRestricted(string source)
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)],
            BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }
}
