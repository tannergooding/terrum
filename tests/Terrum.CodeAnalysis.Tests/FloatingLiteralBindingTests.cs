// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingLiteralBindingTests
{
    [Theory]
    [InlineData("float32", "0.1", 0x3DCCCCCDL)]
    [InlineData("float64", "0.1", 0x3FB999999999999AL)]
    [InlineData("float32", "-0.0", 0x80000000L)]
    [InlineData("float64", "-0.0", long.MinValue)]
    [InlineData("float32", "-0x1.fp+2", 0xC0F80000L)]
    [InlineData("float32", "1.000000059604644775390625", 0x3F800000L)]
    [InlineData("float32", "0x1.fp+2", 0x40F80000L)]
    [InlineData("float32", "0x1_f.0_fP+1_0", 0x46F87800L)]
    [InlineData("float64", "0x1.fp+2", 0x401F000000000000L)]
    [InlineData("float32", "0x1p-149", 1L)]
    [InlineData("float32", "0x3p-150", 2L)]
    [InlineData("float32", "0x1p-150", 0L)]
    [InlineData("float32", "0x1p+128", 0x7F800000L)]
    [InlineData("float64", "0x1p-1074", 1L)]
    [InlineData("float64", "0x3p-1075", 2L)]
    [InlineData("float64", "0x1p-1075", 0L)]
    [InlineData("float64", "0x1p+1024", 0x7FF0000000000000L)]
    [InlineData("float32", "0b1010", 0x41200000L)]
    public void BindsFloatingLiteralsInTheirTargetFormat(string type, string literal, long bits)
    {
        var source = $"public {type} Read() {{ return {literal}; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        var constant = Assert.IsType<BoundConstant>(result.Value);
        Assert.Equal(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64, constant.Type);
        Assert.Equal(bits, constant.Value);
    }

    [Theory]
    [InlineData("float32", "0.1")]
    [InlineData("float64", "0x1.fp+2")]
    public void ExplicitTargetCallDirectlyConstructsUntypedLiteral(string type, string literal)
    {
        var source = $"public {type} Read() {{ return {type}({literal}); }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        Assert.IsType<BoundConstant>(result.Value);
    }

    [Theory]
    [InlineData("float32", "0x1.fp+2", 0x40F80000L)]
    [InlineData("float64", "0.1", 0x3FB999999999999AL)]
    public void FloatingConstantsPersistAndEmitAsScalarValues(string type, string literal, long bits)
    {
        var source = $"public {type} Read() {{ return {literal}; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.Equal(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64,
            Assert.Single(reference.Functions).ReturnType);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Read"], NativeRuntimePolicy.FailFast));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ {type} value = Read(); return 42; }}"), reference);
        Assert.False(consumerCompilation.HasErrors, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = Assert.IsType<BoundProgram>(consumerCompilation.Program);
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var persistedRead = Assert.Single(linked.Functions, static function => function.Name == "Read");
        var constant = Assert.IsType<BoundConstant>(
            Assert.IsType<BoundReturn>(Assert.Single(persistedRead.Body.Statements)).Value);
        Assert.Equal(bits, constant.Value);
    }

    [Fact]
    public void RejectsUntypedFloatingLiteralWithoutTargetContext()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 0.1; }"));

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }

    [Theory]
    [InlineData("public float32 Add(float32 left, float32 right) { return left + right; }")]
    [InlineData("public bool Equal(float32 left, float32 right) { return left == right; }")]
    [InlineData("public bool Less(float64 left, float64 right) { return left < right; }")]
    [InlineData("public float64 Convert(float32 value) { return float64(value); }")]
    public void KeepsFloatingOperationsAndTypedConversionsUnsupported(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData("float32")]
    [InlineData("float64")]
    public void EmitsUnaryFloatingNegation(string type)
    {
        var source = $"public {type} Negate({type} value) {{ return -value; }} "
            + $"int32 Main() {{ _ = Negate(0.1); return 42; }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var negate = Assert.Single(program.Functions, static function => function.Name == "Negate");
        var result = Assert.IsType<BoundReturn>(Assert.Single(negate.Body.Statements));
        Assert.IsType<BoundUnary>(result.Value);
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }
}
