// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Native;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class FloatingLiteralBindingTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float32ToFloat64(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToFloat32(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt64ToFloat32(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt64ToFloat64(ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int32ToFloat32(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long UInt32ToFloat64(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Int64ToFloat32(long value);

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

    [Theory]
    [InlineData("float64", "float64(float32(0.1))", 0x3FB99999A0000000L)]
    [InlineData("float32", "float32(float64(1.000000059604644775390625))", 0x3F800000L)]
    [InlineData("float32", "float32(float64(1e100))", 0x7F800000L)]
    [InlineData("float64", "float64(float32(-0.0))", long.MinValue)]
    public void ConvertsFloatingConstantsBetweenFormats(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        var constant = Assert.IsType<BoundConstant>(result.Value);
        Assert.Equal(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64, constant.Type);
        Assert.Equal(bits, constant.Value);
    }

    [Theory]
    [InlineData("float32", "float32(int32(16777217))", 0x4B800000L)]
    [InlineData("float32", "float32(uint32(4294967295))", 0x4F800000L)]
    [InlineData("float64", "float64(int64(-9007199254740993))", unchecked((long)0xC340000000000000UL))]
    [InlineData("float64", "float64(uint64(18446744073709551615))", 0x43F0000000000000L)]
    public void ConvertsIntegerConstantsToFloatingTargetFormat(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        var constant = Assert.IsType<BoundConstant>(result.Value);
        Assert.Equal(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64, constant.Type);
        Assert.Equal(bits, constant.Value);
    }

    [Fact]
    public void FloatingFormatConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public float64 Widen(float32 value) { return float64(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var producer = ModuleCompiler.CreateProducer(program, ["Widen"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { _ = Widen(0.1); return 42; }"), reference);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var widen = Assert.Single(linked.Functions, static function => function.Name == "Widen");
        var cast = Assert.IsType<BoundCast>(
            Assert.IsType<BoundReturn>(Assert.Single(widen.Body.Statements)).Value);
        Assert.Equal(PrimitiveType.Float32, cast.Operand.Type);
        Assert.Equal(PrimitiveType.Float64, cast.Type);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Widen"], NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void IntegerToFloatingConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public float64 Convert(uint64 value) { return float64(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var producer = ModuleCompiler.CreateProducer(program, ["Convert"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { _ = Convert(uint64(0xFFFFFFFFFFFFFFFF)); return 42; }"), reference);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var convert = Assert.Single(linked.Functions, static function => function.Name == "Convert");
        var cast = Assert.IsType<BoundCast>(
            Assert.IsType<BoundReturn>(Assert.Single(convert.Body.Statements)).Value);
        Assert.Equal(PrimitiveType.UInt64, cast.Operand.Type);
        Assert.Equal(PrimitiveType.Float64, cast.Type);
        Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Convert"], NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void RejectsUntypedFloatingLiteralWithoutTargetContext()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 0.1; }"));

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }

    [Fact]
    public void KeepsFloatingValuesOutsideConstEval()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[ConstEval] int32 Read() { float32 value = 0.1; return 42; }"));

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3024");
    }

    [Theory]
    [InlineData("public float32 Add(float32 left, float32 right) { return left + right; }")]
    [InlineData("public bool Equal(float32 left, float32 right) { return left == right; }")]
    [InlineData("public bool Less(float64 left, float64 right) { return left < right; }")]
    [InlineData("public float32 Convert(char16 value) { return float32(value); }")]
    [InlineData("public int32 Convert(float32 value) { return int32(value); }")]
    public void KeepsFloatingOperationsAndMixedConversionsUnsupported(string source)
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

    [Fact]
    public void EmitsRuntimeFloatingFormatConversions()
    {
        var source = "[NoThrow] float64 Widen(float32 value) { return float64(value); } "
            + "[NoThrow] float32 Narrow(float64 value) { return float32(value); } "
            + "[NoThrow] int32 Main() { float64 wide = Widen(0.1); _ = Narrow(wide); return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void RuntimeFloatingFormatConversionsUseIeeeRounding()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public float64 Widen(float32 value) { return float64(value); } "
            + "[NoThrow] public float32 Narrow(float64 value) { return float32(value); } "
            + "[NoThrow] public float32 FromInt32(int32 value) { return float32(value); } "
            + "[NoThrow] public float64 FromUInt64(uint64 value) { return float64(value); } "
            + "[NoThrow] public float32 FromUInt64Single(uint64 value) { return float32(value); } "
            + "[NoThrow] public float64 FromUInt32(uint32 value) { return float64(value); } "
            + "[NoThrow] public float32 FromInt64(int64 value) { return float32(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-float-conversion-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program,
                    ["Widen", "Narrow", "FromInt32", "FromUInt64", "FromUInt64Single", "FromUInt32", "FromInt64"],
                    NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);

            var widen = Marshal.GetDelegateForFunctionPointer<Float32ToFloat64>(
                NativeLibrary.GetExport(library, "Widen"));
            var narrow = Marshal.GetDelegateForFunctionPointer<Float64ToFloat32>(
                NativeLibrary.GetExport(library, "Narrow"));
            var fromInt32 = Marshal.GetDelegateForFunctionPointer<Int32ToFloat32>(
                NativeLibrary.GetExport(library, "FromInt32"));
            var fromUInt64 = Marshal.GetDelegateForFunctionPointer<UInt64ToFloat64>(
                NativeLibrary.GetExport(library, "FromUInt64"));
            var fromUInt64Single = Marshal.GetDelegateForFunctionPointer<UInt64ToFloat32>(
                NativeLibrary.GetExport(library, "FromUInt64Single"));
            var fromUInt32 = Marshal.GetDelegateForFunctionPointer<UInt32ToFloat64>(
                NativeLibrary.GetExport(library, "FromUInt32"));
            var fromInt64 = Marshal.GetDelegateForFunctionPointer<Int64ToFloat32>(
                NativeLibrary.GetExport(library, "FromInt64"));

            Assert.Equal(0x3FB99999A0000000L, widen(unchecked((int)0x3DCCCCCD)));
            Assert.Equal(0x3F800000, narrow(0x3FF0000010000000L));
            Assert.Equal(unchecked((int)0x7F800000), narrow(0x7FEFFFFFFFFFFFFFL));
            Assert.Equal(int.MinValue, narrow(long.MinValue));
            Assert.Equal(0x4B800000, fromInt32(16_777_217));
            Assert.Equal(0x43E0000000000000L, fromUInt64(0x8000_0000_0000_0400UL));
            Assert.Equal(0x43E0000000000001L, fromUInt64(0x8000_0000_0000_0401UL));
            Assert.Equal(0x43F0000000000000L, fromUInt64(ulong.MaxValue));
            Assert.Equal(0x5F000001, fromUInt64Single(0x8000_0080_0000_0001UL));
            Assert.Equal(0x5F800000, fromUInt64Single(ulong.MaxValue));
            Assert.Equal(0x41EFFFFFFFE00000L, fromUInt32(uint.MaxValue));
            Assert.Equal(unchecked((int)0xDA000000), fromInt64(-9_007_199_254_740_993));
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
            File.Delete(path);
        }
    }
}
