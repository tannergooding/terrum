// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolCompositionTests
{
    private const string Protocols = """
        interface Addition<A, B, C> { static abstract C operator +(A left, B right); }
        interface Multiplication<A, B, C> { static abstract C operator *(A left, B right); }
        interface OtherAddition<A, B, C> { static abstract C operator +(A left, B right); }
        """;

    private const string Value = """
        struct Value : Addition<Value, Value, Value>, Multiplication<Value, Value, Value>,
            OtherAddition<Value, Value, Value> {
            public int32 Number;
            public Value(int32 number) { Number = number; }
            public static Value operator +(Value left, Value right) {
                return new Value(left.Number + right.Number);
            }
            public static Value operator *(Value left, Value right) {
                return new Value(left.Number * right.Number);
            }
        }
        """;

    private const string Helper = """
        U Double<U>(U value, bool twice) where U : Addition<U, U, U>, [Copy] {
            if (twice) { return value + value; }
            return value;
        }
        """;

    [Fact]
    public void TypedStructArgumentInfersCopyHelperThroughDefinitionSiteComposition()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
            }
            T Identity<T>(T value) where T : [Copy] { return value; }
            U Forward<U>(U value) where U : [Copy] { return Identity(value); }
            int32 Main() { return Forward(new Value(42)).Number; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var names = Assert.IsType<BoundProgram>(compilation.Program).Functions.Select(function => function.Name);
        Assert.Contains("Forward<Value>", names);
        Assert.Contains("Identity<Value>", names);
        Assert.DoesNotContain(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.IsExternal || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardHelperCallsUseDeclaredSubsetAndRenamedTypeParameter(bool instantiate)
    {
        var compilation = Compile("""
            T Compose<T>(T value) where T : Addition<T, T, T>, Multiplication<T, T, T>, [Copy] {
                var doubled = Double<T>(value, true);
                return Double<T>(doubled, false) * value;
            }
            """ + Helper, instantiate);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(instantiate ? 2 : 0, program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)));
        Assert.DoesNotContain(program.Structures, structure => structure.Name.Contains('$', StringComparison.Ordinal));
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
        if (instantiate)
        {
            var helper = Assert.Single(program.Functions, function => function.Name == "Double<Value>");
            Assert.Equal(PrimitiveType.Bool, helper.Variables[1].Type);
        }
    }

    [Theory]
    [InlineData("return Double<T>(value, true);", "Multiplication<T, T, T>", "TRM3007")]
    [InlineData("return Double<T>(value, true);", "OtherAddition<T, T, T>", "TRM3007")]
    [InlineData("return Double<Value>(value, true);", "Addition<T, T, T>", "TRM3007")]
    [InlineData("return Double<T?>(value, true);", "Addition<T, T, T>", "TRM3007")]
    [InlineData("return Double<T>(value, value);", "Addition<T, T, T>", "TRM3007")]
    [InlineData("return Double<T>(value);", "Addition<T, T, T>", "TRM3011")]
    [InlineData("return Missing<T>(value);", "Addition<T, T, T>", "TRM3011")]
    [InlineData("T Double = value; return Double<T>(value, true);", "Addition<T, T, T>", "TRM3011")]
    public void InvalidCompositionRejectsAtDefinitionEvenWithRicherConcreteWitness(
        string body, string constraint, string code)
    {
        foreach (var instantiate in new[] { false, true })
        {
            var compilation = Compile("T Compose<T>(T value) where T : " + constraint + ", [Copy] {"
                + body + "}" + Helper, instantiate);
            Assert.True(compilation.HasErrors);
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
            Assert.Null(compilation.Program);
        }
    }

    [Fact]
    public void UnusedHelperBodyCannotBorrowCallersStrongerConstraints()
    {
        var compilation = Compile("""
            T Compose<T>(T value) where T : Addition<T, T, T>, Multiplication<T, T, T>, [Copy] {
                return Double<T>(value, true);
            }
            """ + Helper.Replace("value + value", "value * value", StringComparison.Ordinal), instantiate: false);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HelperEffectIsTakenFromItsDeclarationRatherThanItsBody(bool noThrow)
    {
        var source = """
            interface Addition<A, B, C> {
                [NoThrow] static abstract C operator +(A left, B right);
            }
            [NoThrow] T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                return Identity<T>(value);
            }
            """ + (noThrow ? "[NoThrow] " : "") + """
            U Identity<U>(U value) where U : [Copy] { return value; }
            int32 Main() { return 0; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (noThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Fact]
    public void HelperUsesItsOwnImportsAndReportsItsOwnSourceSnapshot()
    {
        var protocols = SyntaxTree.Parse("namespace Contracts;" + Protocols, "contracts.terrum");
        var caller = SyntaxTree.Parse("""
            using Contracts;
            using Helpers;
            namespace Callers;
            T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                return Double<T>(value, true);
            }
            int32 Main() { return 0; }
            """, "caller.terrum");
        var helper = SyntaxTree.Parse("using Contracts; namespace Helpers;" + Helper, "helper.terrum");
        var compilation = Compilation.Create([caller, helper, protocols]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        helper = SyntaxTree.Parse("using Contracts; namespace Helpers;"
            + Helper.Replace("value + value", "value * value", StringComparison.Ordinal), "helper.terrum");
        compilation = Compilation.Create([caller, helper, protocols]);
        Assert.Contains(compilation.SourceDiagnostics, diagnostic => diagnostic.SyntaxTree == helper
            && diagnostic.Diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpecializationResolvesEachHelpersOwnNamespaceAndImports(bool infer)
    {
        var caller = """
            using Contracts;
            using Helpers;
            namespace Callers;
            T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                return Double(value, true);
            }
            int32 Main() { return Compose(new Value(3)).Number; }
            """;
        var helper = """
            using Contracts;
            using Leaves;
            namespace Helpers;
            U Double<U>(U value, bool twice) where U : Addition<U, U, U>, [Copy] {
                if (twice) { return Identity(value + value); }
                return Identity(value);
            }
            """;
        if (!infer)
        {
            caller = caller.Replace("return Double(value", "return Double<T>(value", StringComparison.Ordinal)
                .Replace("return Compose(new Value", "return Compose<Value>(new Value", StringComparison.Ordinal);
            helper = helper.Replace("return Identity(", "return Identity<U>(", StringComparison.Ordinal);
        }
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace Contracts;" + Protocols + Value),
            SyntaxTree.Parse(caller),
            SyntaxTree.Parse(helper),
            SyntaxTree.Parse("""
                using Contracts;
                namespace Leaves;
                V Identity<V>(V value) where V : [Copy] { return value; }
                """),
        ]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var names = Assert.IsType<BoundProgram>(compilation.Program).Functions.Select(function => function.Name);
        Assert.Contains("Callers.Compose<Contracts.Value>", names);
        Assert.Contains("Helpers.Double<Contracts.Value>", names);
        Assert.Contains("Leaves.Identity<Contracts.Value>", names);
    }

    [Fact]
    public void AmbiguousImportedHelpersDoNotSelectByMatchingConstraints()
    {
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace Contracts;" + Protocols),
            SyntaxTree.Parse("using Contracts; namespace One;" + Helper),
            SyntaxTree.Parse("using Contracts; namespace Two;" + Helper),
            SyntaxTree.Parse("""
                using Contracts;
                using One;
                using Two;
                T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                    return Double<T>(value, true);
                }
                int32 Main() { return 0; }
                """),
        ]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }

    [Theory]
    [InlineData("return value + value;")]
    [InlineData("return Double<T>(value, true);")]
    public void CopyAloneDoesNotGrantOperatorsOrOperatorConstrainedHelpers(string body)
    {
        var compilation = Compile("T Compose<T>(T value) where T : [Copy] {" + body + "}" + Helper,
            instantiate: true);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("")]
    [InlineData("where T : Addition<T, T, T>")]
    public void GenericHelpersStillRequireTheCopyProperty(string constraints)
    {
        var compilation = Compile("T Compose<T>(T value) " + constraints + " { return value; }",
            instantiate: true);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void CopyOnlyHelperRejectsUniqueConcreteValues()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Main() {
                Owner value = new Owner(42);
                return Identity<Owner>(move value).Value;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConcreteCopyStructArgumentsInTemplatesRetainTheirOwnWitnesses(bool instantiate, bool scalar)
    {
        var entry = instantiate
            ? scalar ? "int32 Main() { return Forward(int32(42)); }"
                : "int32 Main() { return Forward(new Value(42)).Number; }"
            : "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(Protocols + Value + """
            T Forward<T>(T value) where T : [Copy] {
                var fixedValue = SelectSecond<T, Value>(value, new Value(1));
                return SelectSecond<Value, T>(fixedValue + fixedValue, value);
            }
            U SelectSecond<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            """ + entry));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(instantiate ? scalar ? 3 : 2 : 0, program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)));
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Fact]
    public void ConcreteNestedCopyFieldsAreRemappedInSymbolicProof()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public int32 Number;
                public Inner(int32 number) { Number = number; }
            }
            struct Outer {
                public Inner Item;
                public Outer(Inner item) { Item = item; }
            }
            T Forward<T>(T value) where T : [Copy] {
                return Second<Outer, T>(new Outer(new Inner(1)), value);
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("Value", false)]
    [InlineData("Other", false)]
    [InlineData("Owner", true)]
    public void ConcreteTypeArgumentsCannotBorrowAnUnrelatedFormalWitnessOrUniqueOwner(
        string selected, bool unique)
    {
        var declaration = unique
            ? "unique struct Owner { public int32 Number; public Owner(int32 number) { Number = number; } }"
            : "struct Other { public int32 Number; public Other(int32 number) { Number = number; } }";
        var protocol = unique ? "" : Protocols + Value;
        var body = selected == "Value"
            ? "return Combine<Value,T>(new Value(1), value);"
            : $"return Combine<{selected},T>(new {selected}(1), value);";
        var compilation = Compilation.Create(SyntaxTree.Parse(protocol + declaration + $$"""
            T Forward<T>(T value) where T : [Copy] { {{body}} }
            U Combine<A, U>(A first, U second)
                where A : Addition<A, A, A>, [Copy] where U : [Copy] { return second; }
            int32 Main() { return 0; }
            """));
        if (selected == "Value")
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3007" or "TRM3011");
            Assert.Null(compilation.Program);
        }
    }

    [Fact]
    public void ConcreteWitnessCannotJustifyAnOperationOnAnUnrelatedTypeParameter()
    {
        var compilation = Compile("""
            T Forward<T>(T value) where T : [Copy] {
                return Combine<T, Value>(value, new Value(1));
            }
            A Combine<A, U>(A first, U second)
                where A : Addition<A, A, A>, [Copy] where U : [Copy] { return first; }
            """, instantiate: true);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteOperationsKeepTheirDeclaredEffectsInGenericDefinitions(bool constructorNoThrow)
    {
        var declaration = constructorNoThrow ? "[NoThrow] " : "";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct Value {
                public int32 Number;
                {{declaration}}public Value(int32 number) { Number = number; }
            }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                return Second<Value, T>(new Value(1), value);
            }
            [NoThrow] U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return 0; }
            """));
        if (constructorNoThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
            Assert.Null(compilation.Program);
        }
    }

    [Fact]
    public void UnreferencedStructsDoNotExhaustGenericProofTypes()
    {
        var source = new StringBuilder();
        for (var i = 0; i < 255; i++)
        {
            _ = source.Append("struct S").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(" { public int32 Value; }");
        }
        _ = source.Append("""
            T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Main() { return 0; }
            """);
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void UnrelatedDeclarationsDoNotExhaustGenericProofFunctions()
    {
        var source = new StringBuilder();
        for (var i = 0; i < 1023; i++)
        {
            _ = source.Append("int32 F").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("() { return 0; }");
        }
        _ = source.Append("""
            T Identity<T>(T value) where T : [Copy] { return value; }
            int32 Main() { return 0; }
            """);
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(1024, Assert.IsType<BoundProgram>(compilation.Program).Functions.Length);
    }

    [Theory]
    [InlineData("Create()", "Value Create() { return new Value(1); }")]
    [InlineData("Value.Create()", "")]
    [InlineData("new Outer(new Value(1)).Get()", "")]
    [InlineData("Outer.Create().Get()", "")]
    [InlineData("created.Get()", "")]
    public void ConcreteFactoryReturnsSeedGenericProofDependencies(string first, string factory)
    {
        var local = first == "created.Get()" ? "var created = Outer.Create();" : "";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value Create() { return new Value(1); }
            }
            struct Outer {
                public Value Item;
                public Outer(Value item) { Item = item; }
                public Value Get() { return Item; }
                public static Outer Create() { return new Outer(new Value(1)); }
            }
            {{factory}}
            T Forward<T>(T value) where T : [Copy] {
                {{local}}
                return Second({{first}}, value);
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = Assert.IsType<BoundProgram>(compilation.Program).Functions;
        Assert.Contains(functions, function => function.Name == "Second<Value, int32>");
        Assert.DoesNotContain(functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteFactoryCallsRetainTheirDeclaredEffectsInGenericProof(bool factoryNoThrow)
    {
        var attribute = factoryNoThrow ? "[NoThrow] " : "";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct Value {
                public int32 Number;
                [NoThrow] public Value(int32 number) { Number = number; }
            }
            {{attribute}}Value Create() { return new Value(1); }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                return Second(Create(), value);
            }
            [NoThrow] U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            int32 Main() { return 0; }
            """));
        if (factoryNoThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
            Assert.Null(compilation.Program);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InferredHelperResultsRetainConcreteMemberAndOperatorBindings(bool siblingScope, bool add)
    {
        var preceding = siblingScope
            ? "{ var created = Outer.Create(); var unused = created.Get(); }" : "";
        var selected = add
            ? "created.Get() + created.Get()" : "created.Get()";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            interface Addition<A, B, C> { static abstract C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value left, Value right) {
                    return new Value(left.Number + right.Number);
                }
            }
            struct Outer {
                public Value Item;
                public Outer(Value item) { Item = item; }
                public static Outer Create() { return new Outer(new Value(1)); }
                public Value Get() { return Item; }
            }
            A Identity<A>(A item) where A : [Copy] { return item; }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            T Forward<T>(T value) where T : [Copy] {
                {{preceding}}
                var first = Outer.Create();
                var created = Identity(first);
                return Second({{selected}}, value);
            }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = Assert.IsType<BoundProgram>(compilation.Program).Functions;
        Assert.Contains(functions, function => function.Name == "Second<Value, int32>");
        Assert.DoesNotContain(functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void GenericFormalShadowsConcreteTypeDuringProofAndInstantiation(
        bool instantiate, bool structArgument, bool staticCall)
    {
        var entry = !instantiate ? "int32 Main() { return 0; }"
            : structArgument ? "int32 Main() { return Read(new Value(1)); }"
            : "int32 Main() { return Read(int32(1)); }";
        var body = staticCall ? "return T.Read();" : "return new T(42).Number;";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct T {
                public int32 Number;
                public T(int32 number) { Number = number; }
                public static int32 Read() { return 42; }
            }
            struct Value {
                public int32 Number;
                public Value(int32 number) { Number = number; }
            }
            int32 Read<T>(T value) where T : [Copy] { {{body}} }
            {{entry}}
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void QualifiedConcreteTypeBypassesSameNamedGenericFormal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Model;
            struct T {
                public int32 Number;
                public T(int32 number) { Number = number; }
                public static int32 Read() { return 1; }
            }
            int32 Probe<T>(T value) where T : [Copy] {
                return new Model.T(41).Number + Model.T.Read();
            }
            int32 Main() { return Probe(int32(1)); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteCyclicManagedFactoriesBindInCopyTemplates(bool instantiate)
    {
        var entry = instantiate
            ? "int32 Main() { return Forward(int32(42)); }"
            : "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class First {
                public mut write Second? Next;
                public First() { Next = null; }
                public mut void Set(write Second next) { Next = next; }
            }
            mut class Second {
                public mut write First? Next;
                public Second() { Next = null; }
                public mut void Set(write First next) { Next = next; }
            }
            U Select<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            T Forward<T>(T value) where T : [Copy] {
                require First.TryCreate(out var first);
                require Second.TryCreate(out var second);
                first.Set(second);
                second.Set(first);
                First read = first;
                First? optional = read;
                require optional is some present && present.Next is some follower;
                require ManagedHeap.Collect() == 0;
                return Select<First, T>(read, value);
            }
            """ + entry));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(2, program.Classes.Length);
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "Cell? empty = null;")]
    [InlineData(true, "Cell? empty = null;")]
    [InlineData(false, "write Cell? empty = null;")]
    [InlineData(true, "write Cell? empty = null;")]
    [InlineData(false, "Model.Cell? empty = null;")]
    [InlineData(true, "Model.Cell? empty = null;")]
    [InlineData(false, "write Model.Cell? empty = null;")]
    [InlineData(true, "write Model.Cell? empty = null;")]
    [InlineData(false, "return Second<Model.Cell?, T>(null, value);")]
    [InlineData(true, "return Second<Model.Cell?, T>(null, value);")]
    [InlineData(false, "return Second<write Model.Cell?, T>(null, value);")]
    [InlineData(true, "return Second<write Model.Cell?, T>(null, value);")]
    public void ConcreteManagedTypeNamesHydrateBeforeFactoryCalls(bool instantiate, string operation)
    {
        var entry = instantiate ? "int32 Main() { return Forward(int32(42)); }"
            : "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            namespace Model;
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            U Second<A, U>(A first, U second)
                where A : [Copy] where U : [Copy] { return second; }
            T Forward<T>(T value) where T : [Copy] {
                {{operation}}
                return value;
            }
            {{entry}}
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        _ = Assert.Single(program.Classes);
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericFormalShadowsConcreteManagedClassDuringProof(bool instantiate)
    {
        var entry = instantiate ? "int32 Main() { return Forward(int32(42)); }"
            : "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            class T {
                public int32 Value;
                public T(int32 value) { Value = value; }
            }
            T Forward<T>(T value) where T : [Copy] {
                require T.TryCreate(42, out var cell);
                return value;
            }
            {{entry}}
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3007" or "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false, "T.TryCreate(1, out var missing);")]
    [InlineData(true, "T.TryCreate(1, out var missing);")]
    [InlineData(false, "var missing = cell.Secret;")]
    [InlineData(true, "var missing = cell.Secret;")]
    [InlineData(false, "Cell read = cell; read.Set(1);")]
    [InlineData(true, "Cell read = cell; read.Set(1);")]
    [InlineData(false, "Cell? optional = cell; var missing = optional.Value;")]
    [InlineData(true, "Cell? optional = cell; var missing = optional.Value;")]
    public void ManagedProofRetainsFormalOpacityAndQualifiedAccess(bool instantiate, string operation)
    {
        var entry = instantiate ? "int32 Main() { return Forward(int32(42)); }"
            : "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            mut class Cell {
                public mut int32 Value;
                private int32 Secret;
                public Cell(int32 value) { Value = value; Secret = value; }
                public mut void Set(int32 value) { Value = value; }
            }
            T Forward<T>(T value) where T : [Copy] {
                require Cell.TryCreate(42, out var cell);
                {{operation}}
                return value;
            }
            {{entry}}
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code is "TRM3007" or "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConcreteManagedFactoryRetainsDeclaredEffectsInNoThrowProof(
        bool constructorNoThrow, bool methodNoThrow)
    {
        var constructorAttribute = constructorNoThrow ? "[NoThrow] " : "";
        var methodAttribute = methodNoThrow ? "[NoThrow] " : "";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            class Cell {
                public int32 Value;
                {{constructorAttribute}}public Cell(int32 value) { Value = value; }
                {{methodAttribute}}public int32 Read() { return Value; }
            }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                if (Cell.TryCreate(1, out var cell)) {
                    var read = cell.Read();
                }
                return value;
            }
            int32 Main() { return 0; }
            """));
        if (constructorNoThrow && methodNoThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
            Assert.Null(compilation.Program);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveHelpersReuseClosedSpecializations(bool mutual)
    {
        var source = """
            T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                return Repeat<T>(value, 3);
            }
            U Repeat<U>(U value, uint32 count) where U : Addition<U, U, U>, [Copy] {
                if (count == 0) { return value; }
                return Repeat<U>(value + value, count - 1);
            }
            """;
        if (mutual)
        {
            source = source.Replace("return Repeat<U>", "return Again<U>", StringComparison.Ordinal)
                + """
                V Again<V>(V value, uint32 count) where V : Addition<V, V, V>, [Copy] {
                    return Repeat<V>(value, count);
                }
                """;
        }
        var compilation = Compile(source, instantiate: true);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(mutual ? 3 : 2, Assert.IsType<BoundProgram>(compilation.Program).Functions.Count(
            function => function.Name.Contains('<', StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    public void TransitivelyCreatedSpecializationsShareTheExistingLimit(int templates)
    {
        var source = new StringBuilder(Protocols).Append(Value);
        _ = source.Append(Value.Replace("Value", "Other", StringComparison.Ordinal));
        for (var i = 0; i < templates; i++)
        {
            _ = source.Append("T Step").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("<T>(T value) where T : Addition<T, T, T>, [Copy] { return ");
            if (i == templates - 1)
            {
                _ = source.Append("value");
            }
            else
            {
                _ = source.Append("Step").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append("<T>(value)");
            }
            _ = source.Append("; }");
        }
        _ = source.Append("int32 Main() { Step0<Other>(new Other(1)); return Step0<Value>(new Value(2)).Number; }");
        var compilation = Compilation.Create(SyntaxTree.Parse(source.ToString()));
        if (templates == 16)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Equal(32, Assert.IsType<BoundProgram>(compilation.Program).Functions.Count(
                function => function.Name.Contains('<', StringComparison.Ordinal)));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        }
    }

    private static Compilation Compile(string templates, bool instantiate)
    {
        var entry = instantiate
            ? "int32 Main() { return Compose<Value>(new Value(3)).Number; }"
            : "int32 Main() { return 0; }";
        return Compilation.Create(SyntaxTree.Parse(Protocols + Value + templates + entry));
    }
}
