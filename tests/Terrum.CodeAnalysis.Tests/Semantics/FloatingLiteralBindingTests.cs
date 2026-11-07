// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class FloatingLiteralBindingTests
{
    private delegate long Float32ToFloat64(int value);
    private delegate int Float64ToFloat32(long value);
    private delegate int UInt64ToFloat32(ulong value);
    private delegate long UInt64ToFloat64(ulong value);
    private delegate int Int32ToFloat32(int value);
    private delegate long UInt32ToFloat64(uint value);
    private delegate int Int64ToFloat32(long value);
    private delegate long Int32ToFloat64(int value);
    private delegate int UInt32ToFloat32(uint value);
    private delegate long Int64ToFloat64(long value);
    private delegate int Char16ToFloat32(ushort value);
    private delegate long Char16ToFloat64(ushort value);
    private delegate int Float64ToInt32(long value);
    private delegate uint Float64ToUInt32(long value);
    private delegate long Float64ToInt64(long value);
    private delegate ulong Float64ToUInt64(long value);
    private delegate int Float32ToInt32(int value);
    private delegate uint Float32ToUInt32(int value);
    private delegate long Float32ToInt64(int value);
    private delegate ulong Float32ToUInt64(int value);
    private delegate int Float32Binary(int left, int right);
    private delegate long Float64Binary(long left, long right);
    private delegate int Float32Copy(int value);
    private delegate long Float64Copy(long value);
    private delegate int Float32Selector(int selector);
    private delegate long Float64Selector(int selector);
    private delegate int Float32FourOperands(int a, int b, int c, int d);
    private delegate long Float64FourOperands(long a, long b, long c, long d);
    private delegate int Float32Comparison(int left, int right);
    private delegate int Float64Comparison(long left, long right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeFloat32ToFloat64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeFloat64ToFloat32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeUInt64ToFloat32(ulong value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeUInt64ToFloat64(ulong value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeInt32ToFloat32(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeUInt32ToFloat64(uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeInt64ToFloat32(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeInt32ToFloat64(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeUInt32ToFloat32(uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeInt64ToFloat64(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeChar16ToFloat32(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeChar16ToFloat64(ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFloat64ToInt32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint NativeFloat64ToUInt32(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long NativeFloat64ToInt64(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong NativeFloat64ToUInt64(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFloat32ToInt32(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint NativeFloat32ToUInt32(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long NativeFloat32ToInt64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong NativeFloat32ToUInt64(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeFloat32Binary(float left, float right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeFloat64Binary(double left, double right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeFloat32Copy(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeFloat64Copy(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeFloat32Selector(int selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeFloat64Selector(int selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float NativeFloat32FourOperands(float a, float b, float c, float d);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double NativeFloat64FourOperands(double a, double b, double c, double d);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFloat32Comparison(float left, float right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeFloat64Comparison(double left, double right);
    [TestCase("float32","0.1",0x3DCCCCCDL)]
    [TestCase("float64","0.1",0x3FB999999999999AL)]
    [TestCase("float32","-0.0",0x80000000L)]
    [TestCase("float64","-0.0",long.MinValue)]
    [TestCase("float32","-0x1.fp+2",0xC0F80000L)]
    [TestCase("float32","1.000000059604644775390625",0x3F800000L)]
    [TestCase("float32","0x1.fp+2",0x40F80000L)]
    [TestCase("float32","0x1_f.0_fP+1_0",0x46F87800L)]
    [TestCase("float64","0x1.fp+2",0x401F000000000000L)]
    [TestCase("float32","0x1p-149",1L)]
    [TestCase("float32","0x3p-150",2L)]
    [TestCase("float32","0x1p-150",0L)]
    [TestCase("float32","0x1p+128",0x7F800000L)]
    [TestCase("float64","0x1p-1074",1L)]
    [TestCase("float64","0x3p-1075",2L)]
    [TestCase("float64","0x1p-1075",0L)]
    [TestCase("float64","0x1p+1024",0x7FF0000000000000L)]
    [TestCase("float32","0b1010",0x41200000L)]
    public void BindsFloatingLiteralsInTheirTargetFormat(string type, string literal, long bits)
    {
        var source = $"public {type} Read() {{ return {literal}; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual1 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual1!;
        }))();
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual2 = result.Value;
            Assert.That(__nunitActual2, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual2!;
        }))();
        Assert.That(constant.Type, Is.EqualTo(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        Assert.That(constant.Value, Is.EqualTo(bits));
    }
    [TestCase("float32","0.1")]
    [TestCase("float64","0x1.fp+2")]
    public void ExplicitTargetCallDirectlyConstructsUntypedLiteral(string type, string literal)
    {
        var source = $"public {type} Read() {{ return {type}({literal}); }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual3 = compilation.Program;
            Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual3!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual4 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual4!;
        }))();
        Assert.That(result.Value, Is.TypeOf<BoundConstant>());
    }
    [TestCase("float32","0x1.fp+2",0x40F80000L)]
    [TestCase("float64","0.1",0x3FB999999999999AL)]
    public void FloatingConstantsPersistAndEmitAsScalarValues(string type, string literal, long bits)
    {
        var source = $"public {type} Read() {{ return {literal}; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual5 = compilation.Program;
            Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual5!;
        }))();

        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        Assert.That(global::System.Linq.Enumerable.Single(reference.Functions).ReturnType, Is.EqualTo(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Read"], NativeRuntimePolicy.FailFast), Is.Not.Empty);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ {type} value = Read(); return 42; }}"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumerProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual6 = consumerCompilation.Program;
            Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual6!;
        }))();
        var consumer = ModuleCompiler.CreateConsumer(consumerProgram, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var persistedRead = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "Read");
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual8 = ((global::System.Func<BoundReturn>)(() => {
    var __nunitActual7 = global::System.Linq.Enumerable.Single(persistedRead.Body.Statements);
    Assert.That(__nunitActual7, Is.TypeOf<BoundReturn>());
    return (BoundReturn)__nunitActual7!; }))().Value;
            Assert.That(__nunitActual8, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual8!;
        }))();
        Assert.That(constant.Value, Is.EqualTo(bits));
    }
    [TestCase("float64","float64(float32(0.1))",0x3FB99999A0000000L)]
    [TestCase("float32","float32(float64(1.000000059604644775390625))",0x3F800000L)]
    [TestCase("float32","float32(float64(1e100))",0x7F800000L)]
    [TestCase("float64","float64(float32(-0.0))",long.MinValue)]
    public void ConvertsFloatingConstantsBetweenFormats(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual9 = compilation.Program;
            Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual9!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual10 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual10, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual10!;
        }))();
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual11 = result.Value;
            Assert.That(__nunitActual11, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual11!;
        }))();
        Assert.That(constant.Type, Is.EqualTo(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        Assert.That(constant.Value, Is.EqualTo(bits));
    }
    [TestCase("float32","float32(int32(16777217))",0x4B800000L)]
    [TestCase("float64","float64(int32(16777217))",0x4170000010000000L)]
    [TestCase("float32","float32(uint32(4294967295))",0x4F800000L)]
    [TestCase("float64","float64(uint32(4294967295))",0x41EFFFFFFFE00000L)]
    [TestCase("float32","float32(int64(-9007199254740993))",0xDA000000L)]
    [TestCase("float64","float64(int64(-9007199254740993))",unchecked((long)0xC340000000000000UL))]
    [TestCase("float32","float32(uint64(18446744073709551615))",0x5F800000L)]
    [TestCase("float64","float64(uint64(18446744073709551615))",0x43F0000000000000L)]
    public void ConvertsIntegerConstantsToFloatingTargetFormat(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual12 = compilation.Program;
            Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual12!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual13 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual13, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual13!;
        }))();
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual14 = result.Value;
            Assert.That(__nunitActual14, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual14!;
        }))();
        Assert.That(constant.Type, Is.EqualTo(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        Assert.That(constant.Value, Is.EqualTo(bits));
    }
    [TestCase("float32")]
    [TestCase("float64")]
    public void ConvertsChar16ConstantsAsUnsignedCodeUnits(string type)
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse($"public {type} Read() {{ return {type}(char16(65535)); }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual15 = compilation.Program;
            Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual15!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual16 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual16, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual16!;
        }))();
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual17 = result.Value;
            Assert.That(__nunitActual17, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual17!;
        }))();
        Assert.That(constant.Type, Is.EqualTo(type == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        var expected = type == "float32"
            ? unchecked((uint)BitConverter.SingleToInt32Bits(65535.0f))
            : BitConverter.DoubleToInt64Bits(65535.0);
        Assert.That(constant.Value, Is.EqualTo(unchecked((long)expected)));
    }
    [TestCase("int32","int32(float64(2147483647.9))",int.MaxValue)]
    [TestCase("int32","int32(float64(-2147483648.5))",int.MinValue)]
    [TestCase("uint32","uint32(float64(-0.5))",0L)]
    [TestCase("uint32","uint32(float64(4294967295.9))",-1L)]
    [TestCase("int64","int64(float64(-0x1p+63))",long.MinValue)]
    [TestCase("uint64","uint64(float64(18446744073709549568))",unchecked((long)0xFFFFFFFFFFFFF800UL))]
    public void ConvertsFloatingConstantsToIntegerTargetFormat(string type, string expression, long bits)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {expression}; }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual18 = compilation.Program;
            Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual18!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual19 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual19, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual19!;
        }))();
        var constant = ((global::System.Func<BoundConstant>)(() => {
            var __nunitActual20 = result.Value;
            Assert.That(__nunitActual20, Is.TypeOf<BoundConstant>());
            return (BoundConstant)__nunitActual20!;
        }))();
        Assert.That(constant.Type, Is.EqualTo(type switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            _ => PrimitiveType.UInt64,
        }));
        Assert.That(constant.Value, Is.EqualTo(bits));
    }
    [TestCase("int32","float64(0x1p+31)")]
    [TestCase("uint32","float64(-1.0)")]
    [TestCase("int64","float64(0x1p+63)")]
    [TestCase("uint64","float64(0x1p+64)")]
    [TestCase("int32","float64(0x1p+1024)")]
    public void LeavesOutOfRangeFloatingConstantsAsCheckedCasts(string type, string expression)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($"public {type} Read() {{ return {type}({expression}); }}"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual21 = compilation.Program;
            Assert.That(__nunitActual21, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual21!;
        }))();
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual22 = global::System.Linq.Enumerable.Single(program.Functions[0].Body.Statements);
            Assert.That(__nunitActual22, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual22!;
        }))();
        var cast = ((global::System.Func<BoundCast>)(() => {
            var __nunitActual23 = result.Value;
            Assert.That(__nunitActual23, Is.TypeOf<BoundCast>());
            return (BoundCast)__nunitActual23!;
        }))();
        Assert.That(cast.Type, Is.EqualTo(type switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            _ => PrimitiveType.UInt64,
        }));
    }

    [Test]
    public void FloatingFormatConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float64 Widen(float32 value) { return float64(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual24 = compilation.Program;
            Assert.That(__nunitActual24, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual24!;
        }))();

        var producer = ModuleCompiler.CreateProducer(program, ["Widen"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { _ = Widen(0.1); return 42; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual25 = consumerCompilation.Program;
            Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual25!;
        }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var widen = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "Widen");
        var cast = ((global::System.Func<BoundCast>)(() => {
            var __nunitActual27 = ((global::System.Func<BoundReturn>)(() => {
    var __nunitActual26 = global::System.Linq.Enumerable.Single(widen.Body.Statements);
    Assert.That(__nunitActual26, Is.TypeOf<BoundReturn>());
    return (BoundReturn)__nunitActual26!; }))().Value;
            Assert.That(__nunitActual27, Is.TypeOf<BoundCast>());
            return (BoundCast)__nunitActual27!;
        }))();
        Assert.That(cast.Operand.Type, Is.EqualTo(PrimitiveType.Float32));
        Assert.That(cast.Type, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Widen"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void IntegerToFloatingConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float64 Convert(uint64 value) { return float64(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual28 = compilation.Program;
            Assert.That(__nunitActual28, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual28!;
        }))();

        var producer = ModuleCompiler.CreateProducer(program, ["Convert"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { _ = Convert(uint64(0xFFFFFFFFFFFFFFFF)); return 42; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual29 = consumerCompilation.Program;
            Assert.That(__nunitActual29, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual29!;
        }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var convert = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "Convert");
        var cast = ((global::System.Func<BoundCast>)(() => {
            var __nunitActual31 = ((global::System.Func<BoundReturn>)(() => {
    var __nunitActual30 = global::System.Linq.Enumerable.Single(convert.Body.Statements);
    Assert.That(__nunitActual30, Is.TypeOf<BoundReturn>());
    return (BoundReturn)__nunitActual30!; }))().Value;
            Assert.That(__nunitActual31, Is.TypeOf<BoundCast>());
            return (BoundCast)__nunitActual31!;
        }))();
        Assert.That(cast.Operand.Type, Is.EqualTo(PrimitiveType.UInt64));
        Assert.That(cast.Type, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Convert"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void Char16ToFloatingConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float32 Narrow(char16 value) { return float32(value); } "
            + " public float64 Widen(char16 value) { return float64(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual32 = compilation.Program;
            Assert.That(__nunitActual32, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual32!;
        }))();

        var producer = ModuleCompiler.CreateProducer(program, ["Narrow", "Widen"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { _ = Narrow(char16(65535)); _ = Widen(char16(65535)); return 42; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual33 = consumerCompilation.Program;
            Assert.That(__nunitActual33, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual33!;
        }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        foreach (var name in new[] { "Narrow", "Widen" })
        {
            var function = global::System.Linq.Enumerable.Single(linked.Functions, item => item.Name == name);
            var cast = ((global::System.Func<BoundCast>)(() => {
                var __nunitActual35 = ((global::System.Func<BoundReturn>)(() => {
    var __nunitActual34 = global::System.Linq.Enumerable.Single(function.Body.Statements);
    Assert.That(__nunitActual34, Is.TypeOf<BoundReturn>());
    return (BoundReturn)__nunitActual34!; }))().Value;
                Assert.That(__nunitActual35, Is.TypeOf<BoundCast>());
                return (BoundCast)__nunitActual35!;
            }))();
            Assert.That(cast.Operand.Type, Is.EqualTo(PrimitiveType.Char16));
            Assert.That(cast.Type, Is.EqualTo(name == "Narrow" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        }
        Assert.That(NativeEmitter.EmitLibrary(program, ["Narrow", "Widen"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void Char16ToFloatingConversionsTreatValuesAsUnsignedCodeUnits()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float32 Narrow(char16 value) { return float32(value); } "
            + " public float64 Widen(char16 value) { return float64(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual36 = compilation.Program;
            Assert.That(__nunitActual36, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual36!;
        }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-char16-float-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["Narrow", "Widen"], NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);
            var narrow = ExportChar16ToFloat32(library, "Narrow");
            var widen = ExportChar16ToFloat64(library, "Widen");
            for (var value = 0; value <= ushort.MaxValue; value++)
            {
                var codeUnit = (ushort)value;
                Assert.That(narrow(codeUnit), Is.EqualTo(BitConverter.SingleToInt32Bits((float)value)));
                Assert.That(widen(codeUnit), Is.EqualTo(BitConverter.DoubleToInt64Bits((double)value)));
            }
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

    [Test]
    public void CopyGenericSpecializationsPreserveFloatingPayloadBits()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Inner {
                public float32 Single;
                public int32 Marker;
                 public Inner(float32 single, int32 marker) { Single = single; Marker = marker; }
            }
            struct Pair {
                public Inner Nested;
                public float64 Wide;
                 public Pair(Inner nested, float64 wide) { Nested = nested; Wide = wide; }
                 public float32 ReadSingle() { return Nested.Single; }
            }
             T Identity<T>(T value) where T : [Copy] { return value; }
             bool TryCreatePair(init ref<Pair> result) {
                result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
             float32 CopyFloat(float32 value) { return value; }
             T Forward<T>(T value) where T : [Copy] {
                if (!TryCreatePair(init var pair)) { return Identity(value); }
                if (CopyFloat(pair.ReadSingle()) != float32(1) || pair.Wide != float64(2)) {
                    return Identity(value);
                }
                return Identity(value);
            }
            public float32 CopyFloat32(float32 value) { return Forward(value); }
            public float64 CopyFloat64(float64 value) { return Forward(value); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual37 = compilation.Program;
            Assert.That(__nunitActual37, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual37!;
        }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-copy-float-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["CopyFloat32", "CopyFloat64"], NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);
            var copyFloat32 = ExportFloat32Copy(library, "CopyFloat32");
            var copyFloat64 = ExportFloat64Copy(library, "CopyFloat64");
            uint[] singleBits = [
                0x00000000, 0x80000000, 0x00000001, 0x007FFFFF, 0x00800000,
                0x3F800000, 0x7F800000, 0xFF800000, 0x7F800001, 0xFFC12345,
            ];
            foreach (var bits in singleBits)
            {
                Assert.That(copyFloat32(unchecked((int)bits)), Is.EqualTo(unchecked((int)bits)));
            }
            ulong[] doubleBits = [
                0x0000000000000000, 0x8000000000000000, 0x0000000000000001,
                0x000FFFFFFFFFFFFF, 0x0010000000000000, 0x3FF0000000000000,
                0x7FF0000000000000, 0xFFF0000000000000, 0x7FF0000000000001,
                0xFFF8123456789ABC,
            ];
            foreach (var bits in doubleBits)
            {
                Assert.That(copyFloat64(unchecked((long)bits)), Is.EqualTo(unchecked((long)bits)));
            }
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

    [Test]
    public void GenericFloatingOutputResultsPreserveExactReturnBits()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            float32 Select32<T>(T value, int32 selector, out T copied, init ref<T> initialized,
                out int32 count, init ref<float64> score) where T : [Copy] {
                copied = value;
                initialized = value;
                count = 2;
                score = float64(3.5);
                if (selector == 0) { return float32(0.0); }
                if (selector == 1) { return float32(-0.0); }
                if (selector == 2) { return float32(0x1p-149); }
                if (selector == 3) { return float32(0x1p+128); }
                if (selector == 4) { return float32(-0x1p+128); }
                return float32(1.000000059604644775390625);
            }
            float64 Select64<T>(T value, int32 selector, out T copied, init ref<T> initialized,
                out int32 count, init ref<float32> score) where T : [Copy] {
                copied = value;
                initialized = value;
                count = 3;
                score = float32(4.5);
                if (selector == 0) { return float64(0.0); }
                if (selector == 1) { return float64(-0.0); }
                if (selector == 2) { return float64(0x1p-1074); }
                if (selector == 3) { return float64(0x1p+1024); }
                if (selector == 4) { return float64(-0x1p+1024); }
                return float64(1.00000000000000011102230246251565404236316680908203125);
            }
            public float32 Read32(int32 selector) {
                mut int32 copied = 0;
                init int32 initialized;
                mut int32 count = 0;
                init float64 score;
                float32 result = Select32<int32>(42, selector, out copied, init initialized,
                    out count, init score);
                require copied == 42 && initialized == 42 && count == 2 && score == float64(3.5);
                return result;
            }
            public float64 Read64(int32 selector) {
                mut int32 copied = 0;
                init int32 initialized;
                mut int32 count = 0;
                init float32 score;
                float64 result = Select64<int32>(42, selector, out copied, init initialized,
                    out count, init score);
                require copied == 42 && initialized == 42 && count == 3 && score == float32(4.5);
                return result;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual38 = compilation.Program;
            Assert.That(__nunitActual38, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual38!;
        }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-generic-output-float-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["Read32", "Read64"], NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);
            var read32 = ExportFloat32Selector(library, "Read32");
            var read64 = ExportFloat64Selector(library, "Read64");
            (int Selector, uint Bits)[] singleCases = [
                (0, 0x00000000),
                (1, 0x80000000),
                (2, 0x00000001),
                (3, 0x7F800000),
                (4, 0xFF800000),
                (5, 0x3F800000),
            ];
            foreach (var (selector, bits) in singleCases)
            {
                Assert.That(read32(selector), Is.EqualTo(unchecked((int)bits)));
            }
            (int Selector, ulong Bits)[] doubleCases = [
                (0, 0x0000000000000000),
                (1, 0x8000000000000000),
                (2, 0x0000000000000001),
                (3, 0x7FF0000000000000),
                (4, 0xFFF0000000000000),
                (5, 0x3FF0000000000000),
            ];
            foreach (var (selector, bits) in doubleCases)
            {
                Assert.That(read64(selector), Is.EqualTo(unchecked((long)bits)));
            }
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

    [Test]
    public void EmitsRuntimeChar16ToFloatingConversions()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
             float32 Narrow(char16 value) { return float32(value); }
             float64 Widen(char16 value) { return float64(value); }
            int32 Main() {
                char16 codeUnit = char16(55296);
                return Narrow(codeUnit) == 55296.0 && Widen(codeUnit) == 55296.0 ? 42 : 0;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual39 = compilation.Program;
            Assert.That(__nunitActual39, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual39!;
        }))();
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void FloatingToIntegerConversionsPersistThroughSemanticModules()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Convert(float64 value) { return int32(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual40 = compilation.Program;
            Assert.That(__nunitActual40, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual40!;
        }))();

        var producer = ModuleCompiler.CreateProducer(program, ["Convert"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Convert(41.9); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual41 = consumerCompilation.Program;
            Assert.That(__nunitActual41, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual41!;
        }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var convert = global::System.Linq.Enumerable.Single(linked.Functions, static function => function.Name == "Convert");
        var cast = ((global::System.Func<BoundCast>)(() => {
            var __nunitActual43 = ((global::System.Func<BoundReturn>)(() => {
    var __nunitActual42 = global::System.Linq.Enumerable.Single(convert.Body.Statements);
    Assert.That(__nunitActual42, Is.TypeOf<BoundReturn>());
    return (BoundReturn)__nunitActual42!; }))().Value;
            Assert.That(__nunitActual43, Is.TypeOf<BoundCast>());
            return (BoundCast)__nunitActual43!;
        }))();
        Assert.That(cast.Operand.Type, Is.EqualTo(PrimitiveType.Float64));
        Assert.That(cast.Type, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(NativeEmitter.EmitLibrary(program, ["Convert"], NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void RejectsUntypedFloatingLiteralWithoutTargetContext()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("public int32 Read() { return 0.1; }"));

        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }

    [TestCase("float16")]
    [TestCase("bfloat16")]
    [TestCase("float32")]
    [TestCase("float64")]
    [TestCase("float128")]
    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void AcceptsFloatingScalarsInConstEval(string type)
    {
        var source = $"[ConstEval] int32 Read() {{ const {type} value = 0.1; return 42; }}";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("public char16 Convert(float32 value) { return char16(value); }")]
    [TestCase("public bool Mixed(float32 left, float64 right) { return left == right; }")]
    [TestCase("public float32 Mixed(float32 left, int32 right) { return left + right; }")]
    public void RejectsUnassignedAndMixedFloatingOperations(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));

        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [TestCase("float16")]
    [TestCase("bfloat16")]
    [TestCase("float32")]
    [TestCase("float64")]
    [TestCase("float128")]
    [TestCase("decimal32")]
    [TestCase("decimal64")]
    [TestCase("decimal128")]
    public void BindsSameFormatFloatingRemainder(string type)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"public {type} Remainder({type} left, {type} right) {{ return left % right; }}"));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = compilation.Program!.Functions.Single();
        var result = (BoundReturn)function.Body.Statements.Single();
        var remainder = (BoundBinary)result.Value!;
        Assert.That(remainder.Operator, Is.EqualTo(TokenKind.Percent));
        Assert.That(remainder.Left.Type, Is.EqualTo(function.ReturnType));
        Assert.That(remainder.Right.Type, Is.EqualTo(function.ReturnType));
    }

    [Test]
    public void EmitsFloatingArithmeticAndIeeeComparisons()
    {
        var source = """
             public float32 Add32(float32 left, float32 right) { return left + right; }
             public float32 Subtract32(float32 left, float32 right) { return left - right; }
             public float32 Multiply32(float32 left, float32 right) { return left * right; }
             public float32 Divide32(float32 left, float32 right) { return left / right; }
             public float32 Nested32(float32 a, float32 b, float32 c, float32 d) { return (a + b) * c - d; }
             public bool Equal32(float32 left, float32 right) { return left == right; }
             public bool NotEqual32(float32 left, float32 right) { return left != right; }
             public bool Less32(float32 left, float32 right) { return left < right; }
             public bool LessEqual32(float32 left, float32 right) { return left <= right; }
             public bool Greater32(float32 left, float32 right) { return left > right; }
             public bool GreaterEqual32(float32 left, float32 right) { return left >= right; }
             public float64 Add64(float64 left, float64 right) { return left + right; }
             public float64 Subtract64(float64 left, float64 right) { return left - right; }
             public float64 Multiply64(float64 left, float64 right) { return left * right; }
             public float64 Divide64(float64 left, float64 right) { return left / right; }
             public float64 Nested64(float64 a, float64 b, float64 c, float64 d) { return (a + b) * c - d; }
             public bool Equal64(float64 left, float64 right) { return left == right; }
             public bool NotEqual64(float64 left, float64 right) { return left != right; }
             public bool Less64(float64 left, float64 right) { return left < right; }
             public bool LessEqual64(float64 left, float64 right) { return left <= right; }
             public bool Greater64(float64 left, float64 right) { return left > right; }
             public bool GreaterEqual64(float64 left, float64 right) { return left >= right; }
            int32 Main() {
                if (Add32(1.5, 2.25) != 3.75 || Subtract64(4.0, 1.5) != 2.5) { return 1; }
                if (!Less32(1.0, 2.0) || !GreaterEqual64(2.0, 2.0)) { return 2; }
                return 42;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual44 = compilation.Program;
            Assert.That(__nunitActual44, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual44!;
        }))();
        var executableCompilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(executableCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, executableCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => {
            var __nunitActual45 = executableCompilation.Program;
            Assert.That(__nunitActual45, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual45!;
        }))(),
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-operators-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            var exports = new[]
            {
                "Add32", "Subtract32", "Multiply32", "Divide32", "Nested32", "Equal32", "NotEqual32",
                "Less32", "LessEqual32", "Greater32", "GreaterEqual32", "Add64", "Subtract64",
                "Multiply64", "Divide64", "Nested64", "Equal64", "NotEqual64", "Less64", "LessEqual64",
                "Greater64", "GreaterEqual64",
            };
            File.WriteAllBytes(path, NativeEmitter.EmitLibrary(program, exports, NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);

            var add32 = ExportFloat32Binary(library, "Add32");
            var subtract32 = ExportFloat32Binary(library, "Subtract32");
            var multiply32 = ExportFloat32Binary(library, "Multiply32");
            var divide32 = ExportFloat32Binary(library, "Divide32");
            var nested32 = ExportFloat32FourOperands(library, "Nested32");
            var equal32 = ExportFloat32Comparison(library, "Equal32");
            var notEqual32 = ExportFloat32Comparison(library, "NotEqual32");
            var less32 = ExportFloat32Comparison(library, "Less32");
            var lessEqual32 = ExportFloat32Comparison(library, "LessEqual32");
            var greater32 = ExportFloat32Comparison(library, "Greater32");
            var greaterEqual32 = ExportFloat32Comparison(library, "GreaterEqual32");
            var add64 = ExportFloat64Binary(library, "Add64");
            var subtract64 = ExportFloat64Binary(library, "Subtract64");
            var multiply64 = ExportFloat64Binary(library, "Multiply64");
            var divide64 = ExportFloat64Binary(library, "Divide64");
            var nested64 = ExportFloat64FourOperands(library, "Nested64");
            var equal64 = ExportFloat64Comparison(library, "Equal64");
            var notEqual64 = ExportFloat64Comparison(library, "NotEqual64");
            var less64 = ExportFloat64Comparison(library, "Less64");
            var lessEqual64 = ExportFloat64Comparison(library, "LessEqual64");
            var greater64 = ExportFloat64Comparison(library, "Greater64");
            var greaterEqual64 = ExportFloat64Comparison(library, "GreaterEqual64");

            Assert.That(add32(0x3FC00000, 0x40100000), Is.EqualTo(0x40700000));
            Assert.That(subtract32(0x40800000, 0x3FC00000), Is.EqualTo(0x40200000));
            Assert.That(multiply32(0x3FC00000, 0x40000000), Is.EqualTo(0x40400000));
            Assert.That(divide32(0x40C00000, 0x40000000), Is.EqualTo(0x40400000));
            Assert.That(nested32(0x3FC00000, 0x40100000, 0x40000000, 0x3F800000), Is.EqualTo(0x40D00000));
            Assert.That(float.IsNaN(BitConverter.Int32BitsToSingle(divide32(0, 0))), Is.True);
            Assert.That(add32(unchecked((int)0xFFC12345), 0x3F800000), Is.EqualTo(unchecked((int)0xFFC12345)));
            Assert.That(equal32(unchecked((int)0x80000000), 0), Is.EqualTo(1));
            Assert.That(equal32(0x7F800001, 0x3F800000), Is.EqualTo(0));
            Assert.That(notEqual32(0x7F800001, 0x3F800000), Is.EqualTo(1));
            Assert.That(less32(0x7F800001, 0x3F800000), Is.EqualTo(0));
            Assert.That(lessEqual32(0x7F800001, 0x3F800000), Is.EqualTo(0));
            Assert.That(greater32(0x7F800001, 0x3F800000), Is.EqualTo(0));
            Assert.That(greaterEqual32(0x7F800001, 0x3F800000), Is.EqualTo(0));
            Assert.That(less32(0x3F800000, 0x40000000), Is.EqualTo(1));
            Assert.That(lessEqual32(0x3F800000, 0x3F800000), Is.EqualTo(1));
            Assert.That(greater32(0x40000000, 0x3F800000), Is.EqualTo(1));
            Assert.That(greaterEqual32(0x3F800000, 0x3F800000), Is.EqualTo(1));

            Assert.That(add64(0x3FF8000000000000L, 0x4002000000000000L), Is.EqualTo(0x400E000000000000L));
            Assert.That(subtract64(0x4010000000000000L, 0x3FF8000000000000L), Is.EqualTo(0x4004000000000000L));
            Assert.That(multiply64(0x3FF8000000000000L, 0x4000000000000000L), Is.EqualTo(0x4008000000000000L));
            Assert.That(divide64(0x4018000000000000L, 0x4000000000000000L), Is.EqualTo(0x4008000000000000L));
            Assert.That(nested64(0x3FF8000000000000L, 0x4002000000000000L, 0x4000000000000000L,
                    0x3FF0000000000000L), Is.EqualTo(0x401A000000000000L));
            Assert.That(double.IsNaN(BitConverter.Int64BitsToDouble(divide64(0, 0))), Is.True);
            Assert.That(add64(unchecked((long)0xFFF8123456789ABCUL), 0x3FF0000000000000L), Is.EqualTo(unchecked((long)0xFFF8123456789ABCUL)));
            Assert.That(equal64(long.MinValue, 0), Is.EqualTo(1));
            Assert.That(equal64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(0));
            Assert.That(notEqual64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(1));
            Assert.That(less64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(0));
            Assert.That(lessEqual64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(0));
            Assert.That(greater64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(0));
            Assert.That(greaterEqual64(0x7FF0000000000001L, 0x3FF0000000000000L), Is.EqualTo(0));
            Assert.That(less64(0x3FF0000000000000L, 0x4000000000000000L), Is.EqualTo(1));
            Assert.That(lessEqual64(0x3FF0000000000000L, 0x3FF0000000000000L), Is.EqualTo(1));
            Assert.That(greater64(0x4000000000000000L, 0x3FF0000000000000L), Is.EqualTo(1));
            Assert.That(greaterEqual64(0x3FF0000000000000L, 0x3FF0000000000000L), Is.EqualTo(1));
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

    [Test]
    public void FloatingArithmeticIgnoresAndRestoresAmbientMxcsr()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float32 Add(float32 left, float32 right) { return left + right; } "
            + " public float32 Multiply(float32 left, float32 right) { return left * right; } "
            + " public float32 Divide(float32 left, float32 right) { return left / right; } "
            + " public float64 Add64(float64 left, float64 right) { return left + right; } "
            + " public float64 Multiply64(float64 left, float64 right) { return left * right; } "
            + " public float64 Divide64(float64 left, float64 right) { return left / right; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual46 = compilation.Program;
            Assert.That(__nunitActual46, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual46!;
        }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-mxcsr-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        const uint floatingControlMask = 0x03000300;
        var originalControl = ReadFloatingControl();
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["Add", "Multiply", "Divide", "Add64", "Multiply64", "Divide64"],
                    NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);
            var add = ExportFloat32Binary(library, "Add");
            var multiply = ExportFloat32Binary(library, "Multiply");
            var divide = ExportFloat32Binary(library, "Divide");
            var add64 = ExportFloat64Binary(library, "Add64");
            var multiply64 = ExportFloat64Binary(library, "Multiply64");
            var divide64 = ExportFloat64Binary(library, "Divide64");
            var alteredControl = (originalControl & ~floatingControlMask) | 0x01000200;
            WriteFloatingControl(alteredControl, floatingControlMask);

            Assert.That(add(0x3F800000, 0x33800000), Is.EqualTo(0x3F800000));
            Assert.That(multiply(0x00800000, 0x3F000000), Is.EqualTo(0x00400000));
            Assert.That(divide(0x3F800000, 0x40400000), Is.EqualTo(0x3EAAAAAB));
            Assert.That(divide(0x00000001, 0x3F800000), Is.EqualTo(0x00000001));
            Assert.That(add64(0x3FF0000000000000L, 0x3CA0000000000000L), Is.EqualTo(0x3FF0000000000000L));
            Assert.That(multiply64(0x0010000000000000L, 0x3FE0000000000000L), Is.EqualTo(0x0008000000000000L));
            Assert.That(divide64(0x3FF0000000000000L, 0x4008000000000000L), Is.EqualTo(0x3FD5555555555555L));
            Assert.That(divide64(0x0000000000000001L, 0x3FF0000000000000L), Is.EqualTo(0x0000000000000001L));
            Assert.That(ReadFloatingControl() & floatingControlMask, Is.EqualTo(alteredControl & floatingControlMask));
        }
        finally
        {
            WriteFloatingControl(originalControl, floatingControlMask);
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
            File.Delete(path);
        }
    }

    [Test]
    public void FloatingConversionsIgnoreAndRestoreAmbientMxcsr()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float32 Narrow(float64 value) { return float32(value); } "
            + " public float64 Widen(float32 value) { return float64(value); } "
            + " public float32 FromInt32(int32 value) { return float32(value); } "
            + " public float32 FromInt64(int64 value) { return float32(value); } "
            + " public float32 FromUInt32(uint32 value) { return float32(value); } "
            + " public float32 FromUInt64(uint64 value) { return float32(value); } "
            + " public float64 FromInt32Wide(int32 value) { return float64(value); } "
            + " public float64 FromInt64Wide(int64 value) { return float64(value); } "
            + " public float64 FromUInt32Wide(uint32 value) { return float64(value); } "
            + " public float64 FromUInt64Wide(uint64 value) { return float64(value); } "
            + "public int32 Catch(float64 value) { unsafe { try { return int32(value); } catch { return 42; } } }"),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual47 = compilation.Program;
            Assert.That(__nunitActual47, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual47!;
        }))();
        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-conversion-mxcsr-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        const uint floatingControlMask = 0x03000300;
        var originalControl = ReadFloatingControl();
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program,
                    ["Narrow", "Widen", "FromInt32", "FromInt64", "FromUInt32", "FromUInt64",
                        "FromInt32Wide", "FromInt64Wide", "FromUInt32Wide", "FromUInt64Wide", "Catch"],
                    NativeRuntimePolicy.CheckedExceptions));
            library = NativeLibrary.Load(path);
            var narrow = ExportFloat64ToFloat32(library, "Narrow");
            var widen = ExportFloat32ToFloat64(library, "Widen");
            var fromInt32 = ExportInt32ToFloat32(library, "FromInt32");
            var fromInt64 = ExportInt64ToFloat32(library, "FromInt64");
            var fromUInt32 = ExportUInt32ToFloat32(library, "FromUInt32");
            var fromUInt64 = ExportUInt64ToFloat32(library, "FromUInt64");
            var fromInt32Wide = ExportInt32ToFloat64(library, "FromInt32Wide");
            var fromInt64Wide = ExportInt64ToFloat64(library, "FromInt64Wide");
            var fromUInt32Wide = ExportUInt32ToFloat64(library, "FromUInt32Wide");
            var fromUInt64Wide = ExportUInt64ToFloat64(library, "FromUInt64Wide");
            var catchConversion = ExportFloat64ToInt32(library, "Catch");
            var alteredControl = (originalControl & ~floatingControlMask) | 0x01000200;
            WriteFloatingControl(alteredControl, floatingControlMask);

            Assert.That(narrow(0x3FF0000010000000L), Is.EqualTo(0x3F800000));
            Assert.That(narrow(0x36A0000000000000L), Is.EqualTo(0x00000001));
            Assert.That(widen(1), Is.EqualTo(0x36A0000000000000L));
            Assert.That(fromInt32(16_777_217), Is.EqualTo(0x4B800000));
            Assert.That(fromInt64(16_777_217), Is.EqualTo(0x4B800000));
            Assert.That(fromUInt32(16_777_217), Is.EqualTo(0x4B800000));
            Assert.That(fromUInt64(16_777_217), Is.EqualTo(0x4B800000));
            Assert.That(fromUInt64(0x8000_0000_0000_0001UL), Is.EqualTo(0x5F000000));
            Assert.That(fromInt32Wide(-16_777_217), Is.EqualTo(BitConverter.DoubleToInt64Bits(-16_777_217.0)));
            Assert.That(fromInt64Wide(9_007_199_254_740_993L), Is.EqualTo(0x4340000000000000L));
            Assert.That(fromUInt32Wide(uint.MaxValue), Is.EqualTo(BitConverter.DoubleToInt64Bits(4_294_967_295.0)));
            Assert.That(fromUInt64Wide(9_007_199_254_740_993UL), Is.EqualTo(0x4340000000000000L));
            Assert.That(fromUInt64Wide(0x8000_0000_0000_0001UL), Is.EqualTo(0x43E0000000000000L));
            Assert.That(catchConversion(0x7FF0000000000001L), Is.EqualTo(42));
            Assert.That(ReadFloatingControl() & floatingControlMask, Is.EqualTo(alteredControl & floatingControlMask));
        }
        finally
        {
            WriteFloatingControl(originalControl, floatingControlMask);
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
            File.Delete(path);
        }
    }
    [TestCase("float32")]
    [TestCase("float64")]
    public void EmitsUnaryFloatingNegation(string type)
    {
        var source = $"public {type} Negate({type} value) {{ return -value; }} "
            + $"int32 Main() {{ _ = Negate(0.1); return 42; }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual48 = compilation.Program;
            Assert.That(__nunitActual48, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual48!;
        }))();
        var negate = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Negate");
        var result = ((global::System.Func<BoundReturn>)(() => {
            var __nunitActual49 = global::System.Linq.Enumerable.Single(negate.Body.Statements);
            Assert.That(__nunitActual49, Is.TypeOf<BoundReturn>());
            return (BoundReturn)__nunitActual49!;
        }))();
        Assert.That(result.Value, Is.TypeOf<BoundUnary>());
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void EmitsRuntimeFloatingFormatConversions()
    {
        var source = " float64 Widen(float32 value) { return float64(value); } "
            + " float32 Narrow(float64 value) { return float32(value); } "
            + " int32 Main() { float64 wide = Widen(0.1); _ = Narrow(wide); return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual50 = compilation.Program;
            Assert.That(__nunitActual50, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual50!;
        }))();
        Assert.That(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void FloatingArithmeticPersistsThroughSemanticModules()
    {
        var producerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float64 Add(float64 left, float64 right) { return left + right; } "
            + " public bool Less(float64 left, float64 right) { return left < right; }"));
        Assert.That(producerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, producerCompilation.Diagnostics));
        var producerProgram = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual51 = producerCompilation.Program;
            Assert.That(__nunitActual51, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual51!;
        }))();
        var producer = ModuleCompiler.CreateProducer(producerProgram, ["Add", "Less"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var clientCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                float64 sum = Add(1.5, 2.25);
                return sum == 3.75 && Less(1.0, 2.0) ? 42 : 1;
            }
            """), reference);
        Assert.That(clientCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, clientCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => {
                var __nunitActual52 = clientCompilation.Program;
                Assert.That(__nunitActual52, Is.TypeOf<BoundProgram>());
                return (BoundProgram)__nunitActual52!;
            }))(), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void RuntimeFloatingFormatConversionsUseIeeeRounding()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public float64 Widen(float32 value) { return float64(value); } "
            + " public float32 Narrow(float64 value) { return float32(value); } "
            + " public float32 FromInt32(int32 value) { return float32(value); } "
            + " public float64 FromUInt64(uint64 value) { return float64(value); } "
            + " public float32 FromUInt64Single(uint64 value) { return float32(value); } "
            + " public float64 FromUInt32(uint32 value) { return float64(value); } "
            + " public float32 FromInt64(int64 value) { return float32(value); } "
            + " public float64 FromInt32Wide(int32 value) { return float64(value); } "
            + " public float32 FromUInt32Single(uint32 value) { return float32(value); } "
            + " public float64 FromInt64Wide(int64 value) { return float64(value); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual53 = compilation.Program;
            Assert.That(__nunitActual53, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual53!;
        }))();

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-conversion-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program,
                    ["Widen", "Narrow", "FromInt32", "FromUInt64", "FromUInt64Single", "FromUInt32", "FromInt64",
                        "FromInt32Wide", "FromUInt32Single", "FromInt64Wide"],
                    NativeRuntimePolicy.FailFast));
            library = NativeLibrary.Load(path);

            var widen = ExportFloat32ToFloat64(library, "Widen");
            var narrow = ExportFloat64ToFloat32(library, "Narrow");
            var fromInt32 = ExportInt32ToFloat32(library, "FromInt32");
            var fromUInt64 = ExportUInt64ToFloat64(library, "FromUInt64");
            var fromUInt64Single = ExportUInt64ToFloat32(library, "FromUInt64Single");
            var fromUInt32 = ExportUInt32ToFloat64(library, "FromUInt32");
            var fromInt64 = ExportInt64ToFloat32(library, "FromInt64");
            var fromInt32Wide = ExportInt32ToFloat64(library, "FromInt32Wide");
            var fromUInt32Single = ExportUInt32ToFloat32(library, "FromUInt32Single");
            var fromInt64Wide = ExportInt64ToFloat64(library, "FromInt64Wide");

            Assert.That(widen(unchecked((int)0x3DCCCCCD)), Is.EqualTo(0x3FB99999A0000000L));
            Assert.That(narrow(0x3FF0000010000000L), Is.EqualTo(0x3F800000));
            Assert.That(narrow(0x7FEFFFFFFFFFFFFFL), Is.EqualTo(unchecked((int)0x7F800000)));
            Assert.That(narrow(long.MinValue), Is.EqualTo(int.MinValue));
            Assert.That(fromInt32(16_777_217), Is.EqualTo(0x4B800000));
            Assert.That(fromUInt64(0x8000_0000_0000_0400UL), Is.EqualTo(0x43E0000000000000L));
            Assert.That(fromUInt64(0x8000_0000_0000_0401UL), Is.EqualTo(0x43E0000000000001L));
            Assert.That(fromUInt64(ulong.MaxValue), Is.EqualTo(0x43F0000000000000L));
            Assert.That(fromUInt64Single(0x8000_0080_0000_0001UL), Is.EqualTo(0x5F000001));
            Assert.That(fromUInt64Single(ulong.MaxValue), Is.EqualTo(0x5F800000));
            Assert.That(fromUInt32(uint.MaxValue), Is.EqualTo(0x41EFFFFFFFE00000L));
            Assert.That(fromInt64(-9_007_199_254_740_993), Is.EqualTo(unchecked((int)0xDA000000)));
            Assert.That(fromInt32Wide(16_777_217), Is.EqualTo(0x4170000010000000L));
            Assert.That(fromUInt32Single(uint.MaxValue), Is.EqualTo(0x4F800000));
            Assert.That(fromInt64Wide(-9_007_199_254_740_993), Is.EqualTo(unchecked((long)0xC340000000000000UL)));
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

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual54 = compilation.Program;
            Assert.That(__nunitActual54, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual54!;
        }))();

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-to-integer-{Guid.NewGuid():N}.dll");
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

            var fromFloat64ToInt32 = ExportFloat64ToInt32(library, "FromFloat64ToInt32");
            var fromFloat64ToUInt32 = ExportFloat64ToUInt32(library, "FromFloat64ToUInt32");
            var fromFloat64ToInt64 = ExportFloat64ToInt64(library, "FromFloat64ToInt64");
            var fromFloat64ToUInt64 = ExportFloat64ToUInt64(library, "FromFloat64ToUInt64");
            var fromFloat32ToInt32 = ExportFloat32ToInt32(library, "FromFloat32ToInt32");
            var fromFloat32ToUInt32 = ExportFloat32ToUInt32(library, "FromFloat32ToUInt32");
            var fromFloat32ToInt64 = ExportFloat32ToInt64(library, "FromFloat32ToInt64");
            var fromFloat32ToUInt64 = ExportFloat32ToUInt64(library, "FromFloat32ToUInt64");

            Assert.That(fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(1.9)), Is.EqualTo(1));
            Assert.That(fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-1.9)), Is.EqualTo(-1));
            Assert.That(fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-0.0)), Is.EqualTo(0));
            Assert.That(fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(-2147483648.9)), Is.EqualTo(int.MinValue));
            Assert.That(fromFloat64ToInt32(BitConverter.DoubleToInt64Bits(2147483647.9)), Is.EqualTo(int.MaxValue));
            Assert.That(fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(-0.5)), Is.EqualTo(0u));
            Assert.That(fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(-0.0)), Is.EqualTo(0u));
            Assert.That(fromFloat64ToUInt32(BitConverter.DoubleToInt64Bits(4294967295.75)), Is.EqualTo(uint.MaxValue));
            Assert.That(fromFloat64ToInt64(BitConverter.DoubleToInt64Bits(long.MinValue)), Is.EqualTo(long.MinValue));
            Assert.That(fromFloat64ToInt64(BitConverter.DoubleToInt64Bits(Math.BitDecrement(9223372036854775808.0))), Is.EqualTo(9_223_372_036_854_774_784L));
            Assert.That(fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(Math.BitDecrement(18446744073709551616.0))), Is.EqualTo(18_446_744_073_709_549_568UL));
            Assert.That(fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(-0.5)), Is.EqualTo(0UL));
            Assert.That(fromFloat64ToUInt64(BitConverter.DoubleToInt64Bits(-0.0)), Is.EqualTo(0UL));
            Assert.That(fromFloat32ToInt32(BitConverter.SingleToInt32Bits(3.75f)), Is.EqualTo(3));
            Assert.That(fromFloat32ToInt32(BitConverter.SingleToInt32Bits(-0.0f)), Is.EqualTo(0));
            Assert.That(fromFloat32ToUInt32(BitConverter.SingleToInt32Bits(-0.5f)), Is.EqualTo(0u));
            Assert.That(fromFloat32ToUInt32(unchecked((int)0x4F7FFFFF)), Is.EqualTo(4_294_967_040u));
            Assert.That(fromFloat32ToInt64(unchecked((int)0xDF000000)), Is.EqualTo(long.MinValue));
            Assert.That(fromFloat32ToUInt64(0x5F000000), Is.EqualTo(9_223_372_036_854_775_808UL));
            Assert.That(fromFloat32ToUInt64(BitConverter.SingleToInt32Bits(1.9f)), Is.EqualTo(1UL));
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
    [TestCase("int32(float64(0x1p+31))")]
    [TestCase("int32(float64(-2147483649.0))")]
    [TestCase("uint32(float64(-1.0))")]
    [TestCase("uint32(float64(0x1p+32))")]
    [TestCase("int64(float64(0x1p+63))")]
    [TestCase("int64(float64(-9223372036854777856))")]
    [TestCase("uint64(float64(-1.0))")]
    [TestCase("uint64(float64(0x1p+64))")]
    [TestCase("int32(float64(0x1p+1024))")]
    [TestCase("uint32(float64(0x1p+1024))")]
    [TestCase("int64(float64(-0x1p+1024))")]
    [TestCase("uint64(float64(0x1p+1024))")]
    [TestCase("int32(float32(0x1p+128))")]
    [TestCase("uint64(float32(0x1p+128))")]
    public void CheckedFloatingToIntegerRangeFailuresCanBeCaught(string expression)
    {
        var source = $"int32 Main() {{ unsafe {{ try {{ _ = {expression}; }} catch {{ return 42; }} }} return 0; }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual55 = compilation.Program;
            Assert.That(__nunitActual55, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual55!;
        }))();
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void CheckedFloatingToIntegerRejectsNanAndInfinities()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Catch(float64 value) { unsafe { try { return int32(value); } catch { return 42; } } }"),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual56 = compilation.Program;
            Assert.That(__nunitActual56, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual56!;
        }))();

        var path = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-float-to-integer-nonfinite-{Guid.NewGuid():N}.dll");
        var library = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path,
                NativeEmitter.EmitLibrary(program, ["Catch"], NativeRuntimePolicy.CheckedExceptions));
            library = NativeLibrary.Load(path);
            var catchConversion = ExportFloat64ToInt32(library, "Catch");

            Assert.That(catchConversion(BitConverter.DoubleToInt64Bits(double.NaN)), Is.EqualTo(42));
            Assert.That(catchConversion(BitConverter.DoubleToInt64Bits(double.PositiveInfinity)), Is.EqualTo(42));
            Assert.That(catchConversion(BitConverter.DoubleToInt64Bits(double.NegativeInfinity)), Is.EqualTo(42));
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

    [Test]
    public void FloatingToIntegerConversionsRemainCheckedOperations()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            " public int32 Convert(float64 value) { return int32(value); }"));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = BoundProgramTestHelpers.MarkNoFailureProof(compilation.Program!, "Convert");
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(program));
    }

    private static Float32ToFloat64 ExportFloat32ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeFloat32ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(BitConverter.Int32BitsToSingle(value)));
    }

    private static Float64ToFloat32 ExportFloat64ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeFloat64ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(BitConverter.Int64BitsToDouble(value)));
    }

    private static UInt64ToFloat32 ExportUInt64ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeUInt64ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(value));
    }

    private static UInt64ToFloat64 ExportUInt64ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeUInt64ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(value));
    }

    private static Int32ToFloat32 ExportInt32ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeInt32ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(value));
    }

    private static UInt32ToFloat64 ExportUInt32ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeUInt32ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(value));
    }

    private static Int64ToFloat32 ExportInt64ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeInt64ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(value));
    }

    private static Int32ToFloat64 ExportInt32ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeInt32ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(value));
    }

    private static UInt32ToFloat32 ExportUInt32ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeUInt32ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(value));
    }

    private static Int64ToFloat64 ExportInt64ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeInt64ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(value));
    }

    private static Char16ToFloat32 ExportChar16ToFloat32(IntPtr library, string name)
    {
        var native = Export<NativeChar16ToFloat32>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(value));
    }

    private static Char16ToFloat64 ExportChar16ToFloat64(IntPtr library, string name)
    {
        var native = Export<NativeChar16ToFloat64>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(value));
    }

    private static Float64ToInt32 ExportFloat64ToInt32(IntPtr library, string name)
    {
        var native = Export<NativeFloat64ToInt32>(library, name);
        return value => native(BitConverter.Int64BitsToDouble(value));
    }

    private static Float64ToUInt32 ExportFloat64ToUInt32(IntPtr library, string name)
    {
        var native = Export<NativeFloat64ToUInt32>(library, name);
        return value => native(BitConverter.Int64BitsToDouble(value));
    }

    private static Float64ToInt64 ExportFloat64ToInt64(IntPtr library, string name)
    {
        var native = Export<NativeFloat64ToInt64>(library, name);
        return value => native(BitConverter.Int64BitsToDouble(value));
    }

    private static Float64ToUInt64 ExportFloat64ToUInt64(IntPtr library, string name)
    {
        var native = Export<NativeFloat64ToUInt64>(library, name);
        return value => native(BitConverter.Int64BitsToDouble(value));
    }

    private static Float32ToInt32 ExportFloat32ToInt32(IntPtr library, string name)
    {
        var native = Export<NativeFloat32ToInt32>(library, name);
        return value => native(BitConverter.Int32BitsToSingle(value));
    }

    private static Float32ToUInt32 ExportFloat32ToUInt32(IntPtr library, string name)
    {
        var native = Export<NativeFloat32ToUInt32>(library, name);
        return value => native(BitConverter.Int32BitsToSingle(value));
    }

    private static Float32ToInt64 ExportFloat32ToInt64(IntPtr library, string name)
    {
        var native = Export<NativeFloat32ToInt64>(library, name);
        return value => native(BitConverter.Int32BitsToSingle(value));
    }

    private static Float32ToUInt64 ExportFloat32ToUInt64(IntPtr library, string name)
    {
        var native = Export<NativeFloat32ToUInt64>(library, name);
        return value => native(BitConverter.Int32BitsToSingle(value));
    }

    private static Float32Binary ExportFloat32Binary(IntPtr library, string name)
    {
        var native = Export<NativeFloat32Binary>(library, name);
        return (left, right) => BitConverter.SingleToInt32Bits(
            native(BitConverter.Int32BitsToSingle(left), BitConverter.Int32BitsToSingle(right)));
    }

    private static Float64Binary ExportFloat64Binary(IntPtr library, string name)
    {
        var native = Export<NativeFloat64Binary>(library, name);
        return (left, right) => BitConverter.DoubleToInt64Bits(
            native(BitConverter.Int64BitsToDouble(left), BitConverter.Int64BitsToDouble(right)));
    }

    private static Float32Copy ExportFloat32Copy(IntPtr library, string name)
    {
        var native = Export<NativeFloat32Copy>(library, name);
        return value => BitConverter.SingleToInt32Bits(native(BitConverter.Int32BitsToSingle(value)));
    }

    private static Float64Copy ExportFloat64Copy(IntPtr library, string name)
    {
        var native = Export<NativeFloat64Copy>(library, name);
        return value => BitConverter.DoubleToInt64Bits(native(BitConverter.Int64BitsToDouble(value)));
    }

    private static Float32Selector ExportFloat32Selector(IntPtr library, string name)
    {
        var native = Export<NativeFloat32Selector>(library, name);
        return selector => BitConverter.SingleToInt32Bits(native(selector));
    }

    private static Float64Selector ExportFloat64Selector(IntPtr library, string name)
    {
        var native = Export<NativeFloat64Selector>(library, name);
        return selector => BitConverter.DoubleToInt64Bits(native(selector));
    }

    private static Float32FourOperands ExportFloat32FourOperands(IntPtr library, string name)
    {
        var native = Export<NativeFloat32FourOperands>(library, name);
        return (a, b, c, d) => BitConverter.SingleToInt32Bits(native(
            BitConverter.Int32BitsToSingle(a), BitConverter.Int32BitsToSingle(b),
            BitConverter.Int32BitsToSingle(c), BitConverter.Int32BitsToSingle(d)));
    }

    private static Float64FourOperands ExportFloat64FourOperands(IntPtr library, string name)
    {
        var native = Export<NativeFloat64FourOperands>(library, name);
        return (a, b, c, d) => BitConverter.DoubleToInt64Bits(native(
            BitConverter.Int64BitsToDouble(a), BitConverter.Int64BitsToDouble(b),
            BitConverter.Int64BitsToDouble(c), BitConverter.Int64BitsToDouble(d)));
    }

    private static Float32Comparison ExportFloat32Comparison(IntPtr library, string name)
    {
        var native = Export<NativeFloat32Comparison>(library, name);
        return (left, right) => native(
            BitConverter.Int32BitsToSingle(left), BitConverter.Int32BitsToSingle(right));
    }

    private static Float64Comparison ExportFloat64Comparison(IntPtr library, string name)
    {
        var native = Export<NativeFloat64Comparison>(library, name);
        return (left, right) => native(
            BitConverter.Int64BitsToDouble(left), BitConverter.Int64BitsToDouble(right));
    }

    private static T Export<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static uint ReadFloatingControl()
    {
        var error = ControlFloatingPointEnvironment(out var current, 0, 0);
        if (error != 0)
        {
            throw new InvalidOperationException($"_controlfp_s failed with error {error}.");
        }
        return current;
    }

    private static void WriteFloatingControl(uint value, uint mask)
    {
        var error = ControlFloatingPointEnvironment(out _, value, mask);
        if (error != 0)
        {
            throw new InvalidOperationException($"_controlfp_s failed with error {error}.");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ucrtbase.dll", EntryPoint = "_controlfp_s", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ControlFloatingPointEnvironment(out uint current, uint value, uint mask);
}
