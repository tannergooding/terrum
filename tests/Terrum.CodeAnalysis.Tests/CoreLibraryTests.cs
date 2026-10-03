// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
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
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)));
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
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(consumerBytes.AsSpan(4)));
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
    [InlineData("bool", BoundRuntimePolicy.FailFast)]
    [InlineData("bool", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("char16", BoundRuntimePolicy.FailFast)]
    [InlineData("char16", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("int32", BoundRuntimePolicy.FailFast)]
    [InlineData("int32", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("uint32", BoundRuntimePolicy.FailFast)]
    [InlineData("uint32", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("int64", BoundRuntimePolicy.FailFast)]
    [InlineData("int64", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("uint64", BoundRuntimePolicy.FailFast)]
    [InlineData("uint64", BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Empty(program.Structures);
        Assert.Equal($"{name}.#get:Value", Assert.Single(program.Functions,
            function => function.PropertyName == "Value").Name);
        Assert.Null(Assert.Single(program.Functions,
            function => function.Name == $"{name}.get_Value").PropertyName);
        var exports = new[] { $"{name}.Identity", $"{name}.#get:Value", $"{name}.ReadPrivate" };
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4)));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(source),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4)));
        var linked = ModuleCompiler.Link(consumer, producer);
        var policy = (NativeRuntimePolicy)runtimePolicy;
        Assert.Equal(7, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var oldProducer = (byte[])producer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldProducer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.CreateReference(oldProducer));
        var oldConsumer = (byte[])consumer.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(oldConsumer.AsSpan(4), 37);
        Assert.Throws<ModuleFormatException>(() => ModuleCompiler.Link(oldConsumer, producer));
    }

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.False(original.HasErrors, string.Join(Environment.NewLine, original.Diagnostics));
        Assert.False(revised.HasErrors, string.Join(Environment.NewLine, revised.Diagnostics));
        string[] exports = ["int32.Identity", "int32.#get:Value"];
        var firstProgram = Assert.IsType<BoundProgram>(original.Program);
        var secondProgram = Assert.IsType<BoundProgram>(revised.Program);
        var first = ModuleCompiler.CreateProducer(firstProgram, exports);
        var second = ModuleCompiler.CreateProducer(secondProgram, exports);
        var source = SyntaxTree.Parse("int32 Main() { return int32.Identity(7).Value; }");
        var consumerCompilation = Compilation.Create(source,
            ModuleCompiler.CreateReference(first), runtimePolicy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), first);
        Assert.Equal(8, NativeJit.Execute(ModuleCompiler.Link(consumer, second), policy));

        var nativeFirst = NativeModuleCompiler.CreateProducer(firstProgram, exports, "Core", policy);
        var nativeSecond = NativeModuleCompiler.CreateProducer(secondProgram, exports, "Core", policy);
        var nativeCompilation = Compilation.Create(source,
            NativeModuleCompiler.CreateGraphReference([nativeFirst]), runtimePolicy);
        Assert.False(nativeCompilation.HasErrors, string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeCompilation.Program), [nativeFirst], policy);
        Assert.Equal(7, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeFirst],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(8, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeSecond],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("char16")]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
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
            Assert.True(Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)]).HasErrors);
        }
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("char16")]
    [InlineData("int32")]
    [InlineData("uint32")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public void CorePrimitiveGetterCannotShadowIntrinsicBacking(string name)
    {
        var source = $"public struct {name} {{ private {name} value; "
            + $"public {name} value {{ get {{ return value; }} }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3001");
    }

    [Fact]
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
        Assert.False(core.HasErrors, string.Join(Environment.NewLine, core.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(core.Program),
            ["int32.get_Hidden"]);
        var consumer = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return int32(7).Hidden; }"),
            ModuleCompiler.CreateReference(producer));
        Assert.True(consumer.HasErrors);
    }

    [Theory]
    [InlineData("bool", BoundRuntimePolicy.FailFast)]
    [InlineData("bool", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("char16", BoundRuntimePolicy.FailFast)]
    [InlineData("char16", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("int32", BoundRuntimePolicy.FailFast)]
    [InlineData("int32", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("uint32", BoundRuntimePolicy.FailFast)]
    [InlineData("uint32", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("int64", BoundRuntimePolicy.FailFast)]
    [InlineData("int64", BoundRuntimePolicy.CheckedExceptions)]
    [InlineData("uint64", BoundRuntimePolicy.FailFast)]
    [InlineData("uint64", BoundRuntimePolicy.CheckedExceptions)]
    public void ImplementedPrimitiveApisExecuteAcrossModuleBoundary(string name,
        BoundRuntimePolicy runtimePolicy)
    {
        var source = $"public struct {name} {{ private {name} value; public static {name} Identity({name} input) {{ return input.value; }} public {name} Read() {{ return value; }} }}";
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)], runtimePolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            [$"{name}.Identity", $"{name}.Read"]);
        var body = name == "bool" ? "if (bool.Identity(true).Read()) { return 7; } return 0;"
            : $"return int32({name}.Identity({name}(7)).Read());";
        var consumer = Compilation.Create(SyntaxTree.Parse($"int32 Main() {{ {body} }}"),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(consumer.Program), producer);
        Assert.Equal(7, NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            runtimePolicy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions));
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

    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void DistinctCorePrimitiveMethodsExecuteFromSeparateSources(
        BoundRuntimePolicy runtimePolicy)
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse(
                "public struct int32 { private int32 value; public static int32 Next(int32 input) { return input.value + 1; } }"),
            SyntaxTree.Parse(
                "public struct uint32 { private uint32 value; public static uint32 Next(uint32 input) { return input.value + 1; } }"),
        ], runtimePolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program),
            ["int32.Next", "uint32.Next"]);
        var consumer = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return int32.Next(39) + int32(uint32.Next(uint32(1))); }"),
            ModuleCompiler.CreateReference(producer), runtimePolicy);
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumer.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            runtimePolicy == BoundRuntimePolicy.FailFast
                ? NativeRuntimePolicy.FailFast : NativeRuntimePolicy.CheckedExceptions));
    }

    [Fact]
    public void CheckedCorePrimitiveMethodFailureReachesConsumerCatch()
    {
        var compilation = Compilation.CreateCoreLibrary([
            SyntaxTree.Parse(
                "public struct int32 { private int32 value; public int32 Next() { return value + 1; } }"),
        ], BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            Assert.IsType<BoundProgram>(compilation.Program), ["int32.Next"]);
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
        Assert.False(consumer.HasErrors, string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerBytes = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumer.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumerBytes, producer),
            NativeRuntimePolicy.CheckedExceptions));
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
    [InlineData("interface int32<T, R> { static R operator +(T left); }")]
    [InlineData("namespace Example; interface bool<T, R> { static R operator +(T left); }")]
    [InlineData("interface Numeric<int32, R> { static R operator +(int32 left); }")]
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
    [InlineData("interface int32<T, R> { static R operator +(T left); }")]
    public void CorePrimitiveShapesAreRestricted(string source)
    {
        var compilation = Compilation.CreateCoreLibrary([SyntaxTree.Parse(source)],
            BoundRuntimePolicy.FailFast);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }
}
