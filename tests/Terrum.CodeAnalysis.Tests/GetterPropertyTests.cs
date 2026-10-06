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

public sealed class GetterPropertyTests
{
    internal const string ValueSource = """
        struct Counter {
            int32 _value;
            public Counter(int32 value) { _value = value; }
            public int32 Value { [NoThrow] get { return _value; } }
            public int32 get_Value() { return 1; }
        }
        class Box {
            int32 _value;
            public Box(int32 value) { _value = value; }
            public int32 Value { get { require _value > 0; return _value; } }
        }
        int32 Main() {
            Counter value = new Counter(40);
            require Box.TryCreate(value.Value, out var box);
            return box.Value + value.get_Value() + 1;
        }
        """;

    [Fact]
    public void GetterHasDistinctMemberIdentityAndAccessorEffects()
    {
        var tree = SyntaxTree.Parse(ValueSource);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(ValueSource, tree.Root.ToFullString());
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var getter = Assert.Single(program.Functions, function => function.PropertyName == "Value"
            && function.DeclaringStruct == "Counter");
        Assert.Equal("Counter.#get:Value", getter.Name);
        Assert.True(getter.IsNoThrow);
        Assert.False(Assert.Single(program.Functions, function => function.PropertyName == "Value"
            && function.DeclaringStruct == "Box").IsNoThrow);
        Assert.Null(Assert.Single(program.Functions, function => function.Name == "Counter.get_Value").PropertyName);
        Assert.Equal(42, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        var index = program.Functions.IndexOf(getter);
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(index, getter with { PropertyName = "Other" }),
        }));
    }

    [Fact]
    public void StaticComputedGetterHasNoReceiverAndUsesTypeQualifiedAndUnqualifiedLookup()
    {
        const string source = """
            public struct Counter {
                public int32 Placeholder;
                public static int32 Base { [NoThrow] get { return 40; } }
                public static int32 LocalValue {
                    get {
                        int32 local = 42;
                        return local;
                    }
                }
                public static int32 Direct => 2;
                public static int32 Value {
                    [NoThrow] get { return Base; }
                }
                public static int32 UnsafeValue { unsafe get { return 1; } }
                public static int32 Read() { return Value + Direct; }
                public int32 ReadStaticValue() { return Counter.Value; }
            }
            int32 Main() { return Counter.Read() + unsafe(Counter.UnsafeValue); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        foreach (var propertyName in new[] { "Base", "Value" })
        {
            var getter = Assert.Single(program.Functions,
                function => function.DeclaringStruct == "Counter" && function.PropertyName == propertyName);
            Assert.Equal("Counter.#get:" + propertyName, getter.Name);
            Assert.True(getter.IsStatic);
            Assert.Equal(0, getter.ParameterCount);
            Assert.True(getter.IsNoThrow);
        }
        var localGetter = Assert.Single(program.Functions,
            static function => function.DeclaringStruct == "Counter" && function.PropertyName == "LocalValue");
        Assert.Equal(0, localGetter.ParameterCount);
        Assert.Equal("local", localGetter.Variables[0].Name);
        Assert.True(Assert.Single(program.Functions,
            static function => function.DeclaringStruct == "Counter"
                && function.PropertyName == "UnsafeValue").RequiresUnsafeCaller);
        var staticGetterIndex = program.Functions.IndexOf(Assert.Single(program.Functions,
            static function => function.DeclaringStruct == "Counter" && function.PropertyName == "Value"));
        var staticGetter = program.Functions[staticGetterIndex];
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(staticGetterIndex, staticGetter with { IsStatic = false }),
        }));
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(staticGetterIndex, staticGetter with {
                Name = "Counter.#get:Other",
            }),
        }));
        Assert.Equal(43, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData("", "A.Counter.Value")]
    [InlineData("A.Counter value", "value.InstanceValue")]
    public void GetterLookupDoesNotFallBackToAnotherOwner(string parameters, string access)
    {
        var compilation = Compilation.CreateLibrary([
            SyntaxTree.Parse("namespace A; public struct Counter { public int32 Placeholder; }"),
            SyntaxTree.Parse("""
                namespace N.A;
                public struct Counter {
                    public static int32 Value { get { return 42; } }
                    public int32 InstanceValue { get { return 43; } }
                }
                """),
            SyntaxTree.Parse($"namespace N; int32 Probe({parameters}) {{ return {access}; }}"),
        ]);

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3006");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32 result = Counter.Value;")]
    [InlineData("var result = Counter.Value;")]
    public void StaticGetterDoesNotTreatAnUnresolvedLocalAsAType(string declaration)
    {
        var source = "public struct Counter { public static int32 Value { get { return 42; } } } "
            + "int32 Main() { init var Counter; " + declaration
            + " Counter = int32(0); return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3006");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void StaticComputedGetterDoesNotAddClassPropertySupport()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Box {
                public int32 Placeholder;
                public static int32 Value { get { return 42; } }
            }
            int32 Main() { return 0; }
            """));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3001");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GetterVerifierRejectsFieldNameCollisionsForStructsAndClasses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ValueSource));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        foreach (var owner in new[] { "Counter", "Box" })
        {
            var index = program.Functions.IndexOf(Assert.Single(program.Functions,
                function => function.PropertyName == "Value" && function.DeclaringStruct == owner));
            var getter = program.Functions[index];
            var otherGetter = getter with {
                Name = owner + ".#get:Other",
                PropertyName = "Other",
            };
            var collidingGetter = getter with {
                Name = owner + ".#get:_value",
                PropertyName = "_value",
            };
            _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
                Functions = program.Functions.SetItem(index, otherGetter).Add(collidingGetter),
            }));
        }
    }

    [Theory]
    [InlineData("public int32 Value { get; }", "TRM1002")]
    [InlineData("public int32 Value { get { return _value; } set { _value = 1; } }", "TRM1002")]
    [InlineData("public int32 Value { [DoesNotReturn] get { return _value; } }", "TRM3001")]
    [InlineData("public mut int32 Value { get { return _value; } }", "TRM3001")]
    [InlineData("public static Counter Value { get { return new Counter(1); } }", "TRM3007")]
    [InlineData("public void Value { get { return; } }", "TRM3007")]
    [InlineData("public int32 _value { get { return _value; } }", "TRM3001")]
    [InlineData("[NoThrow] public int32 Value { get { return _value; } }", "TRM3001")]
    public void UnsupportedGetterShapesDiagnose(string declaration, string code)
    {
        var source = "struct Counter { int32 _value; public Counter(int32 value) { _value = value; } "
            + declaration + " } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void PropertyReadIsNotAnAssignableField()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(ValueSource.Replace(
            "return box.Value + value.get_Value() + 1;",
            "value.Value = 1; return box.Value + value.get_Value() + 1;",
            StringComparison.Ordinal)));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericCopyConstraintDoesNotInventInstanceGetterWitnesses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Counter {
                int32 _value;
                public Counter(int32 value) { _value = value; }
                public int32 Value { get { return _value; } }
            }
            int32 Read<T>(T input) where T : [Copy] { return input.Value; }
            int32 Main() { return Read(new Counter(42)); }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GetterEffectsAreCheckedAtTheAccessSite()
    {
        var source = """
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { get { require _value > 0; return _value; } }
                [NoThrow] public int32 Read() { return this.Value; }
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExportedGetterServicesSemanticAndNativeConsumersAsync(NativeRuntimePolicy policy)
    {
        const string Library = """
            public struct Counter {
                public int32 _value;
                public Counter(int32 value) { _value = value; }
                public static Counter Create() { return new Counter(42); }
                public int32 Value { [NoThrow] get { return _value; } }
                public int32 get_Value() { return 1; }
            }
            """;
        const string Consumer = "int32 Main() { Counter value = Counter.Create(); return value.Value; }";
        string[] exports = ["Counter.Create", "Counter.#get:Value"];
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Library), boundPolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var producer = ModuleCompiler.CreateProducer(program, exports);
        Assert.Equal(61,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(producer), boundPolicy);
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal("Value", Assert.Single(linked.Functions,
            function => function.Name == "Counter.#get:Value").PropertyName);
        Assert.Equal(42, NativeJit.Execute(linked, policy));
        Assert.NotEmpty(NativeEmitter.Emit(linked, policy));

        var nativeProducer = NativeModuleCompiler.CreateProducer(program, exports, "NodeB", policy);
        var nativeCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), boundPolicy);
        Assert.False(nativeCompilation.HasErrors,
            string.Join(Environment.NewLine, nativeCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeCompilation.Program), [nativeProducer], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateClassGetterSurvivesManagedGraphIntermediateAsync(NativeRuntimePolicy policy)
    {
        const string Leaf = "namespace Source; public int32 Bias() { return 42; }";
        const string Middle = """
            using Source;
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public int32 Value { [NoThrow] get { return _value; } }
            }
            public int32 Read() {
                require Box.TryCreate(Bias(), out var box);
                return box.Value;
            }
            """;
        var leaf = NativeModuleCompiler.CreateProducer(
            CompileLibrary(Leaf, policy), ["Source.Bias"], "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(CompileLibrary(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([leaf])), ["Read"], "NodeA", [leaf], policy);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            NativeModuleCompiler.CreateGraphReference([middle]), (BoundRuntimePolicy)policy);
        Assert.False(rootCompilation.HasErrors, string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var root = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(rootCompilation.Program), [middle], policy);
        NativeModuleCompiler.ValidateGraph(root, [middle, leaf]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [leaf, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
    }

    [Fact]
    public void GetterVersionAlsoPreservesPrivateInitMode()
    {
        var program = CompileLibrary("""
            struct Counter {
                int32 _value;
                public Counter(int32 value) { _value = value; }
                public int32 Value { [NoThrow] get { return _value; } }
            }
            bool Fill(init ref<int32> value) { value = 42; return true; }
            public int32 Read() {
                require Fill(init var result);
                return new Counter(result).Value;
            }
            """, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(program, ["Read"]);
        Assert.Equal(61,BinaryPrimitives.ReadInt32LittleEndian(producer.AsSpan(4, 4)));
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return Read(); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(consumerCompilation.HasErrors,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(consumerCompilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.True(Assert.Single(linked.Functions, function => function.Name == "$producer.Fill")
            .Variables[0].IsInit);
        Assert.Equal(42, NativeJit.Execute(linked, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void PrivateGetterOnSharedPublicStructSurvivesSemanticLinking()
    {
        var program = CompileLibrary("""
            public struct Counter {
                public int32 ValueField;
                public Counter(int32 value) { ValueField = value; }
                int32 Value { [NoThrow] get { return ValueField; } }
                public static Counter Create() { return new Counter(42); }
                public int32 Read() { return Value; }
            }
            """, NativeRuntimePolicy.FailFast);
        var producer = ModuleCompiler.CreateProducer(program, ["Counter.Create", "Counter.Read"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { Counter value = Counter.Create(); return value.Read(); }
            """), ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        Assert.Equal(42, NativeJit.Execute(linked, NativeRuntimePolicy.FailFast));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null)
    {
        var tree = SyntaxTree.Parse(source);
        var compilation = reference is null
            ? Compilation.CreateLibrary(tree, (BoundRuntimePolicy)policy)
            : Compilation.CreateLibrary(tree, reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }
}
