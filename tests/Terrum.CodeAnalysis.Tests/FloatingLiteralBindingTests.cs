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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int32ToFloat64(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UInt32ToFloat32(uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Int64ToFloat64(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float64ToInt32(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float64ToUInt32(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float64ToInt64(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float64ToUInt64(long value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Float32ToInt32(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Float32ToUInt32(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long Float32ToInt64(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Float32ToUInt64(int value);

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
    [InlineData("float64", "float64(int32(16777217))", 0x4170000010000000L)]
    [InlineData("float32", "float32(uint32(4294967295))", 0x4F800000L)]
    [InlineData("float64", "float64(uint32(4294967295))", 0x41EFFFFFFFE00000L)]
    [InlineData("float32", "float32(int64(-9007199254740993))", 0xDA000000L)]
    [InlineData("float64", "float64(int64(-9007199254740993))", unchecked((long)0xC340000000000000UL))]
    [InlineData("float32", "float32(uint64(18446744073709551615))", 0x5F800000L)]
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

    [Theory]
    [InlineData("int32", "int32(float64(2147483647.9))", int.MaxValue)]
    [InlineData("int32", "int32(float64(-2147483648.5))", int.MinValue)]
    [InlineData("uint32", "uint32(float64(-0.5))", 0L)]
    [InlineData("uint32", "uint32(float64(4294967295.9))", -1L)]
    [InlineData("int64", "int64(float64(-0x1p+63))", long.MinValue)]
    [InlineData("uint64", "uint64(float64(18446744073709549568))", unchecked((long)0xFFFFFFFFFFFFF800UL))]
    public void ConvertsFloatingConstantsToIntegerTargetFormat(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        var constant = Assert.IsType<BoundConstant>(result.Value);
        Assert.Equal(type switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            _ => PrimitiveType.UInt64,
        }, constant.Type);
        Assert.Equal(bits, constant.Value);
    }

    [Theory]
    [InlineData("int32", "float64(0x1p+31)")]
    [InlineData("uint32", "float64(-1.0)")]
    [InlineData("int64", "float64(0x1p+63)")]
    [InlineData("uint64", "float64(0x1p+64)")]
    [InlineData("int32", "float64(0x1p+1024)")]
    public void LeavesOutOfRangeFloatingConstantsAsCheckedCasts(string type, string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {type}({expression}); }}"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var result = Assert.IsType<BoundReturn>(Assert.Single(program.Functions[0].Body.Statements));
        var cast = Assert.IsType<BoundCast>(result.Value);
        Assert.Equal(type switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            _ => PrimitiveType.UInt64,
        }, cast.Type);
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
    public void FloatingToIntegerConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Convert(float64 value) { return int32(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var producer = ModuleCompiler.CreateProducer(program, ["Convert"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Convert(41.9); }"), reference);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var convert = Assert.Single(linked.Functions, static function => function.Name == "Convert");
        var cast = Assert.IsType<BoundCast>(
            Assert.IsType<BoundReturn>(Assert.Single(convert.Body.Statements)).Value);
        Assert.Equal(PrimitiveType.Float64, cast.Operand.Type);
        Assert.Equal(PrimitiveType.Int32, cast.Type);
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
    [InlineData("public char16 Convert(float32 value) { return char16(value); }")]
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
            + "[NoThrow] public float32 FromInt64(int64 value) { return float32(value); } "
            + "[NoThrow] public float64 FromInt32Wide(int32 value) { return float64(value); } "
            + "[NoThrow] public float32 FromUInt32Single(uint32 value) { return float32(value); } "
            + "[NoThrow] public float64 FromInt64Wide(int64 value) { return float64(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-float-conversion-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program,
                    ["Widen", "Narrow", "FromInt32", "FromUInt64", "FromUInt64Single", "FromUInt32", "FromInt64",
                        "FromInt32Wide", "FromUInt32Single", "FromInt64Wide"],
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
            var fromInt32Wide = Marshal.GetDelegateForFunctionPointer<Int32ToFloat64>(
                NativeLibrary.GetExport(library, "FromInt32Wide"));
            var fromUInt32Single = Marshal.GetDelegateForFunctionPointer<UInt32ToFloat32>(
                NativeLibrary.GetExport(library, "FromUInt32Single"));
            var fromInt64Wide = Marshal.GetDelegateForFunctionPointer<Int64ToFloat64>(
                NativeLibrary.GetExport(library, "FromInt64Wide"));

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
            Assert.Equal(0x4170000010000000L, fromInt32Wide(16_777_217));
            Assert.Equal(0x4F800000, fromUInt32Single(uint.MaxValue));
            Assert.Equal(unchecked((long)0xC340000000000000UL),
                fromInt64Wide(-9_007_199_254_740_993));
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

    [Fact]
    public void RuntimeFloatingToIntegerConversionsTruncateTowardZero()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 FromFloat64ToInt32(float64 value) { return int32(value); } "
            + "public uint32 FromFloat64ToUInt32(float64 value) { return uint32(value); } "
            + "public int64 FromFloat64ToInt64(float64 value) { return int64(value); } "
            + "public uint64 FromFloat64ToUInt64(float64 value) { return uint64(value); } "
            + "public int32 FromFloat32ToInt32(float32 value) { return int32(value); } "
            + "public uint32 FromFloat32ToUInt32(float32 value) { return uint32(value); } "
            + "public int64 FromFloat32ToInt64(float32 value) { return int64(value); } "
            + "public uint64 FromFloat32ToUInt64(float32 value) { return uint64(value); }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-float-to-integer-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program,
                    ["FromFloat64ToInt32", "FromFloat64ToUInt32", "FromFloat64ToInt64",
                        "FromFloat64ToUInt64", "FromFloat32ToInt32", "FromFloat32ToUInt32",
                        "FromFloat32ToInt64", "FromFloat32ToUInt64"],
                    NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);

            var fromFloat64ToInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                NativeLibrary.GetExport(library, "FromFloat64ToInt32"));
            var fromFloat64ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt32>(
                NativeLibrary.GetExport(library, "FromFloat64ToUInt32"));
            var fromFloat64ToInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToInt64>(
                NativeLibrary.GetExport(library, "FromFloat64ToInt64"));
            var fromFloat64ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float64ToUInt64>(
                NativeLibrary.GetExport(library, "FromFloat64ToUInt64"));
            var fromFloat32ToInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToInt32>(
                NativeLibrary.GetExport(library, "FromFloat32ToInt32"));
            var fromFloat32ToUInt32 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt32>(
                NativeLibrary.GetExport(library, "FromFloat32ToUInt32"));
            var fromFloat32ToInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToInt64>(
                NativeLibrary.GetExport(library, "FromFloat32ToInt64"));
            var fromFloat32ToUInt64 = Marshal.GetDelegateForFunctionPointer<Float32ToUInt64>(
                NativeLibrary.GetExport(library, "FromFloat32ToUInt64"));

            Assert.Equal(1, fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(1.9)));
            Assert.Equal(-1, fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-1.9)));
            Assert.Equal(0, fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.Equal(int.MinValue,
                fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-2147483648.9)));
            Assert.Equal(int.MaxValue,
                fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(2147483647.9)));
            Assert.Equal(0u, fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(-0.5)));
            Assert.Equal(0u, fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.Equal(uint.MaxValue,
                fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(4294967295.75)));
            Assert.Equal(long.MinValue, fromFloat64ToInt64(BitConverter.DoubleToInt64Bits(long.MinValue)));
            Assert.Equal(9_223_372_036_854_774_784L,
                fromFloat64ToInt64(BitConverter.DoubleToInt64Bits(Math.BitDecrement(9223372036854775808.0))));
            Assert.Equal(18_446_744_073_709_549_568UL,
                fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(Math.BitDecrement(18446744073709551616.0))));
            Assert.Equal(0UL, fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(-0.5)));
            Assert.Equal(0UL, fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.Equal(3, fromFloat32ToInt32(BitConverter.SingleToInt32Bits(3.75f)));
            Assert.Equal(0, fromFloat32ToInt32(BitConverter.SingleToInt32Bits(-0.0f)));
            Assert.Equal(0u, fromFloat32ToUInt32(BitConverter.SingleToInt32Bits(-0.5f)));
            Assert.Equal(4_294_967_040u, fromFloat32ToUInt32(unchecked((int)0x4F7FFFFF)));
            Assert.Equal(long.MinValue, fromFloat32ToInt64(unchecked((int)0xDF000000)));
            Assert.Equal(9_223_372_036_854_775_808UL,
                fromFloat32ToUInt64(0x5F000000));
            Assert.Equal(1UL, fromFloat32ToUInt64(BitConverter.SingleToInt32Bits(1.9f)));
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

    [Theory]
    [InlineData("int32(float64(0x1p+31))")]
    [InlineData("int32(float64(-2147483649.0))")]
    [InlineData("uint32(float64(-1.0))")]
    [InlineData("uint32(float64(0x1p+32))")]
    [InlineData("int64(float64(0x1p+63))")]
    [InlineData("int64(float64(-9223372036854777856))")]
    [InlineData("uint64(float64(-1.0))")]
    [InlineData("uint64(float64(0x1p+64))")]
    [InlineData("int32(float64(0x1p+1024))")]
    [InlineData("uint32(float64(0x1p+1024))")]
    [InlineData("int64(float64(-0x1p+1024))")]
    [InlineData("uint64(float64(0x1p+1024))")]
    [InlineData("int32(float32(0x1p+128))")]
    [InlineData("uint64(float32(0x1p+128))")]
    public void CheckedFloatingToIntegerRangeFailuresCanBeCaught(string expression)
    {
        var source = $"int32 Main() {{ unsafe {{ try {{ _ = {expression}; }} catch {{ return 42; }} }} return 0; }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions));
    }

    [Fact]
    public void CheckedFloatingToIntegerRejectsNanAndInfinities()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Catch(float64 value) { unsafe { try { return int32(value); } catch { return 42; } } }"),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        var path = Path.Combine(Path.GetTempPath(), $"terrum-float-to-integer-nonfinite-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["Catch"], NativeRuntimePolicy.CheckedExceptions));
            library = NativeLibrary.Load(path);
            var catchConversion = Marshal.GetDelegateForFunctionPointer<Float64ToInt32>(
                NativeLibrary.GetExport(library, "Catch"));

            Assert.Equal(42, catchConversion(BitConverter.DoubleToInt64Bits(double.NaN)));
            Assert.Equal(42, catchConversion(BitConverter.DoubleToInt64Bits(double.PositiveInfinity)));
            Assert.Equal(42, catchConversion(BitConverter.DoubleToInt64Bits(double.NegativeInfinity)));
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

    [Fact]
    public void FloatingToIntegerConversionsRemainCheckedOperations()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[NoThrow] public int32 Convert(float64 value) { return int32(value); }"));

        Assert.True(compilation.HasErrors);
    }
}
