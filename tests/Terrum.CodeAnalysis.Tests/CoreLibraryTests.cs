// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

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

    [Test]
    public void CorePrimitiveApisRetainTheirIntrinsicTypeAcrossModuleBinding()
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Core)]);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(program.Structures, Is.Empty);
        var producer = ModuleCompiler.CreateProducer(program,
            ["int32.TryParse", "int32.TryFormat", "int32.Identity", "int32.ReadBacking"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)), Is.EqualTo(76));
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
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = consumer.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumerBytes.AsSpan(4)), Is.EqualTo(76));
        var linked = ModuleCompiler.Link(consumerBytes, producer);
        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var oldVersion = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldVersion.AsSpan(4), 36);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldVersion));
        var oldConsumer = (byte[])consumerBytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldConsumer.AsSpan(4), 33);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(oldConsumer, producer));

        var ordinary = Compilation.CreateLibrary(
            SyntaxTree.Parse("public int32 Value() { return 42; }"));
        Assert.That(ordinary.HasErrors, Is.False);
        var ordinaryProducer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = ordinary.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), ["Value"]);
        BinaryPrimitives.WriteInt32LittleEndian(ordinaryProducer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(ordinaryProducer));
    }
    [TestCase("bool")]
    [TestCase("char16")]
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void ImplementedPrimitivesCanExposeTheirIntrinsicBackingValue(string name)
    {
        var source = $"public struct {name} {{ private {name} value; public {name} Identity() {{ return value; }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)],
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Structures, Is.Empty);
    }
    [TestCase("bool",BoundRuntimePolicy.FailFast)]
    [TestCase("bool",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("char16",BoundRuntimePolicy.FailFast)]
    [TestCase("char16",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("int32",BoundRuntimePolicy.FailFast)]
    [TestCase("int32",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("uint32",BoundRuntimePolicy.FailFast)]
    [TestCase("uint32",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("int64",BoundRuntimePolicy.FailFast)]
    [TestCase("int64",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("uint64",BoundRuntimePolicy.FailFast)]
    [TestCase("uint64",BoundRuntimePolicy.CheckedExceptions)]
    public void CorePrimitiveGetterRetainsIdentityAndServicesSeparateModule(
        string name, BoundRuntimePolicy runtimePolicy)
    {
        var library = $$"""
            public struct {{name}} {
                private {{name}} value;
                public static {{name}} Identity({{name}} input) { return input.value; }
                public {{name}} Value { [NoThrow] get { return value; } }
                private {{name}} PrivateValue { [NoThrow] get { return value; } }
                public {{name}} ReadPrivate() { return PrivateValue; }
                public {{name}} get_Value() { return value; }
            }
            """;
        var source = name == "bool"
            ? "int32 Main() { if (bool.Identity(true).Value && bool.Identity(true).ReadPrivate()) { return 7; } return 0; }"
            : $"int32 Main() {{ {name} value = {name}.Identity({name}(7)); return int32(value.Value) + int32(value.ReadPrivate()) - 7; }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(library)], runtimePolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.That(program.Structures, Is.Empty);
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.PropertyName == "Value").Name, Is.EqualTo($"{name}.#get:Value"));
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == $"{name}.get_Value").PropertyName, Is.Null);
        var exports = new[] { $"{name}.Identity", $"{name}.#get:Value", $"{name}.ReadPrivate" };
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)), Is.EqualTo(76));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(source),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = consumerCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), producer);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4)), Is.EqualTo(76));
        var linked = ModuleCompiler.Link(consumer, producer);
        var policy = (NativeRuntimePolicy)runtimePolicy;
        Assert.That(NativeJit.Execute(linked, policy), Is.EqualTo(7));
        Assert.That(NativeEmitter.Emit(linked, policy), Is.Not.Empty);

        var oldProducer = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldProducer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldProducer));
        var oldConsumer = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldConsumer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(oldConsumer, producer));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public async Task CorePrimitiveGetterSupportsNativeModuleAndCompatibleReplacementAsync(
        BoundRuntimePolicy runtimePolicy)
    {
        const string Original = """
            public struct int32 {
                private int32 value;
                public static int32 Identity(int32 input) { return input.value; }
                public int32 Value { [NoThrow] get { return value; } }
            }
            """;
        var replacement = Original.Replace("get { return value; }", "get { return 8; }",
            StringComparison.Ordinal);
        var policy = (NativeRuntimePolicy)runtimePolicy;
        var original = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Original)], runtimePolicy);
        var revised = Compilation.CreateCoreLibrary([SyntaxTree.Parse(replacement)], runtimePolicy);
        Assert.That(original.HasErrors, Is.False, string.Join(Environment.NewLine, original.Diagnostics));
        Assert.That(revised.HasErrors, Is.False, string.Join(Environment.NewLine, revised.Diagnostics));
        string[] exports = ["int32.Identity", "int32.#get:Value"];
        var firstProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = original.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var secondProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = revised.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        var first = ModuleCompiler.CreateProducer(firstProgram, exports);
        var second = ModuleCompiler.CreateProducer(secondProgram, exports);
        var source = SyntaxTree.Parse("int32 Main() { return int32.Identity(7).Value; }");
        var consumerCompilation = Compilation.Create(source,
            ModuleCompiler.CreateReference(first), runtimePolicy);
        Assert.That(consumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = consumerCompilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))(), first);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, second), policy), Is.EqualTo(8));

        var nativeFirst = NativeModuleCompiler.CreateProducer(firstProgram, exports, "Core", policy);
        var nativeSecond = NativeModuleCompiler.CreateProducer(secondProgram, exports, "Core", policy);
        var nativeCompilation = Compilation.Create(source,
            NativeModuleCompiler.CreateGraphReference([nativeFirst]), runtimePolicy);
        Assert.That(nativeCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = nativeCompilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))(), [nativeFirst], policy);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeFirst],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(7));
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeSecond],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(8));
    }
    [TestCase("bool")]
    [TestCase("char16")]
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void CorePrimitiveGetterRejectsUnsupportedShapes(string name)
    {
        string[] declarations =
        [
            $"public static {name} Value {{ get {{ return value; }} }}",
            $"public mut {name} Value {{ get {{ return value; }} }}",
            $"public {name} Value {{ get; }}",
            $"public {name} Value {{ get {{ return value; }} set {{ value = value; }} }}",
        ];
        foreach (var declaration in declarations)
        {
            var source = $"public struct {name} {{ private {name} value; {declaration} }}";
            Assert.That(Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)]).HasErrors, Is.True);
        }
    }
    [TestCase("bool")]
    [TestCase("char16")]
    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public void CorePrimitiveGetterCannotShadowIntrinsicBacking(string name)
    {
        var source = $"public struct {name} {{ private {name} value; "
            + $"public {name} value {{ get {{ return value; }} }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)]);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3001"),Is.True);
    }

    [Test]
    public void CorePrimitiveGetterVisibilityIsNotInferredFromConventionalMethodName()
    {
        const string Source = """
            public struct int32 {
                private int32 value;
                private int32 Hidden { get { return value; } }
                public int32 get_Hidden() { return value; }
            }
            """;
        var core = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Source)]);
        Assert.That(core.HasErrors, Is.False, string.Join(Environment.NewLine, core.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = core.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))(),
            ["int32.get_Hidden"]);
        var consumer = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return int32(7).Hidden; }"),
            ModuleCompiler.CreateReference(producer));
        Assert.That(consumer.HasErrors, Is.True);
    }
    [TestCase("bool",BoundRuntimePolicy.FailFast)]
    [TestCase("bool",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("char16",BoundRuntimePolicy.FailFast)]
    [TestCase("char16",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("int32",BoundRuntimePolicy.FailFast)]
    [TestCase("int32",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("uint32",BoundRuntimePolicy.FailFast)]
    [TestCase("uint32",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("int64",BoundRuntimePolicy.FailFast)]
    [TestCase("int64",BoundRuntimePolicy.CheckedExceptions)]
    [TestCase("uint64",BoundRuntimePolicy.FailFast)]
    [TestCase("uint64",BoundRuntimePolicy.CheckedExceptions)]
    public void ImplementedPrimitiveApisExecuteAcrossModuleBoundary(string name,
        BoundRuntimePolicy runtimePolicy)
    {
        var source = $"public struct {name} {{ private {name} value; public static {name} Identity({name} input) {{ return input.value; }} public {name} Read() {{ return value; }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)], runtimePolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))(),
            [$"{name}.Identity", $"{name}.Read"]);
        var body = name == "bool" ? "if (bool.Identity(true).Read()) { return 7; } return 0;"
            : $"return int32({name}.Identity({name}(7)).Read());";
        var consumer = Compilation.Create(SyntaxTree.Parse($"int32 Main() {{ {body} }}"),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = consumer.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            runtimePolicy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(7));
    }

    [Test]
    public void DistinctCorePrimitiveDeclarationsBindAcrossSources()
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse("public struct int32 { private int32 value; public int32 Self() { return value; } }"),
            SyntaxTree.Parse("public struct uint32 { private uint32 value; public uint32 Self() { return value; } }"),
        ], BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))().Functions.Length, Is.EqualTo(2));
    }
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void DistinctCorePrimitiveMethodsExecuteFromSeparateSources(
        BoundRuntimePolicy runtimePolicy)
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse(
                "public struct int32 { private int32 value; public static int32 Next(int32 input) { return input.value + 1; } }"),
            SyntaxTree.Parse(
                "public struct uint32 { private uint32 value; public static uint32 Next(uint32 input) { return input.value + 1; } }"),
        ], runtimePolicy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))(),
            ["int32.Next", "uint32.Next"]);
        var consumer = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return int32.Next(39) + int32(uint32.Next(uint32(1))); }"),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual15 = consumer.Program; Assert.That(__nunitActual15, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual15!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            runtimePolicy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void CheckedCorePrimitiveMethodFailureReachesConsumerCatch()
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse(
                "public struct int32 { private int32 value; public int32 Next() { return value + 1; } }"),
        ], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual16 = compilation.Program; Assert.That(__nunitActual16, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual16!; }))(), ["int32.Next"]);
        var consumer = Compilation.Create(SyntaxTree.Parse(
            """
            int32 Main() {
                unsafe {
                    try { return int32(2147483647).Next(); }
                    catch { return 42; }
                }
            }
            """), ModuleCompiler.CreateReference(producer),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(consumer.HasErrors, Is.False, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual17 = consumer.Program; Assert.That(__nunitActual17, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual17!; }))(), producer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            NativeRuntimePolicy.CheckedExceptions), Is.EqualTo(42));
    }

    [Test]
    public void PrimitiveOwnerVerificationDoesNotReinterpretHistoricalNominalLayouts()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "[Blittable] [Layout(LayoutKind.Sequential)] public struct Cell { public int32 Value; public int32 Read() { return Value; } }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual18 = compilation.Program; Assert.That(__nunitActual18, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual18!; }))();
        var function = global::System.Linq.Enumerable.Single(program.Functions);
        var nominal = program with {
            Structures = program.Structures.SetItem(0, program.Structures[0] with { Name = "int32" }),
            Functions = program.Functions.SetItem(0, function with {
                Name = "int32.Read",
                DeclaringStruct = "int32",
            }),
        };
        SemanticVerifier.VerifyLibrary(nominal);
    }

    [Test]
    public void PrimitiveReceiverCannotBeForgedWritable()
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(Core)],
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual19 = compilation.Program; Assert.That(__nunitActual19, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual19!; }))();
        var index = program.Functions.IndexOf(global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "int32.TryFormat"));
        var function = program.Functions[index];
        var forged = program with {
            Functions = program.Functions.SetItem(index, function with {
                Variables = function.Variables.SetItem(0,
                    function.Variables[0] with { IsMutable = true }),
            }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(forged));
    }
    [TestCase("struct int32 { private int32 value; }")]
    [TestCase("namespace Example; struct int32 { private int32 value; }")]
    [TestCase("class int32 { public int32 value; }")]
    [TestCase("struct bool { private bool value; }")]
    [TestCase("interface int32<T, R> { static R operator +(T left); }")]
    [TestCase("namespace Example; interface bool<T, R> { static R operator +(T left); }")]
    [TestCase("interface Numeric<int32, R> { static R operator +(int32 left); }")]
    [TestCase("int32 Identity<int32>(int32 input) { return input; }")]
    public void OrdinaryPackagesCannotDeclarePrimitiveBindings(string source)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3003"),Is.True);
    }
    [TestCase("struct int32 { }")]
    [TestCase("struct int32 { private uint32 value; }")]
    [TestCase("struct int32 { private int32 first; private int32 second; }")]
    [TestCase("struct int32 { public int32 value; }")]
    [TestCase("struct int32 { private static int32 value; }")]
    [TestCase("struct int32 { private int32 value = 0; }")]
    [TestCase("struct int32 { private int32 value; public int32(int32 v) { value = v; } }")]
    [TestCase("struct int32 { private int32 value; public static int32 operator +(int32 x, int32 y) { return x; } }")]
    [TestCase("struct int32 { private int32 value; public mut int32 Update() { return value; } }")]
    [TestCase("struct int32 { private int32 value; } struct int32 { private int32 value; }")]
    [TestCase("namespace Example; struct int32 { private int32 value; }")]
    [TestCase("interface int32<T, R> { static R operator +(T left); }")]
    public void CorePrimitiveShapesAreRestricted(string source)
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)],
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
}
