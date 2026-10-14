// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolCompositionTests
{
    private const string Protocols = """
        interface Addition<A, B, C> { static C operator +(A left, B right); }
        interface Multiplication<A, B, C> { static C operator *(A left, B right); }
        interface OtherAddition<A, B, C> { static C operator +(A left, B right); }
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

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var names = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions.Select(function => function.Name);
        Assert.That(names, Does.Contain("Forward<Value>"));
        Assert.That(names, Does.Contain("Identity<Value>"));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))().Functions,function => function.IsExternal || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ForwardHelperCallsUseDeclaredSubsetAndRenamedTypeParameter(bool instantiate)
    {
        var compilation = Compile("""
            T Compose<T>(T value) where T : Addition<T, T, T>, Multiplication<T, T, T>, [Copy] {
                var doubled = Double<T>(value, true);
                return Double<T>(doubled, false) * value;
            }
            """ + Helper, instantiate);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)), Is.EqualTo(instantiate ? 2 : 0));
        Assert.That(global::System.Linq.Enumerable.Any(program.Structures,structure => structure.Name.Contains('$', StringComparison.Ordinal)),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
        if (instantiate)
        {
            var helper = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Double<Value>");
            Assert.That(helper.Variables[1].Type, Is.EqualTo(PrimitiveType.Bool));
        }
    }
    [TestCase("return Double<T>(value, true);","Multiplication<T, T, T>","TRM3007")]
    [TestCase("return Double<T>(value, true);","OtherAddition<T, T, T>","TRM3007")]
    [TestCase("return Double<Value>(value, true);","Addition<T, T, T>","TRM3007")]
    [TestCase("return Double<T?>(value, true);","Addition<T, T, T>","TRM3007")]
    [TestCase("return Double<T>(value, value);","Addition<T, T, T>","TRM3007")]
    [TestCase("return Double<T>(value);","Addition<T, T, T>","TRM3011")]
    [TestCase("return Missing<T>(value);","Addition<T, T, T>","TRM3011")]
    [TestCase("T Double = value; return Double<T>(value, true);","Addition<T, T, T>","TRM3011")]
    public void InvalidCompositionRejectsAtDefinitionEvenWithRicherConcreteWitness(
        string body, string constraint, string code)
    {
        foreach (var instantiate in new[] { false, true })
        {
            var compilation = Compile("T Compose<T>(T value) where T : " + constraint + ", [Copy] {"
                + body + "}" + Helper, instantiate);
            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }

    [Test]
    public void UnusedHelperBodyCannotBorrowCallersStrongerConstraints()
    {
        var compilation = Compile("""
            T Compose<T>(T value) where T : Addition<T, T, T>, Multiplication<T, T, T>, [Copy] {
                return Double<T>(value, true);
            }
            """ + Helper.Replace("value + value", "value * value", StringComparison.Ordinal), instantiate: false);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void HelperEffectIsTakenFromItsDeclarationRatherThanItsBody(bool noThrow)
    {
        var source = """
            interface Addition<A, B, C> {
                [NoThrow] static C operator +(A left, B right);
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        }
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        helper = SyntaxTree.Parse("using Contracts; namespace Helpers;"
            + Helper.Replace("value + value", "value * value", StringComparison.Ordinal), "helper.terrum");
        compilation = Compilation.Create([caller, helper, protocols]);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.SourceDiagnostics,diagnostic => diagnostic.SyntaxTree == helper
            && diagnostic.Diagnostic.Code == "TRM3007"),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var names = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Functions.Select(function => function.Name);
        Assert.That(names, Does.Contain("Callers.Compose<Contracts.Value>"));
        Assert.That(names, Does.Contain("Helpers.Double<Contracts.Value>"));
        Assert.That(names, Does.Contain("Leaves.Identity<Contracts.Value>"));
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
    }
    [TestCase("return value + value;")]
    [TestCase("return Double<T>(value, true);")]
    public void CopyAloneDoesNotGrantOperatorsOrOperatorConstrainedHelpers(string body)
    {
        var compilation = Compile("T Compose<T>(T value) where T : [Copy] {" + body + "}" + Helper,
            instantiate: true);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("")]
    [TestCase("where T : Addition<T, T, T>")]
    public void GenericHelpersStillRequireTheCopyProperty(string constraints)
    {
        var compilation = Compile("T Compose<T>(T value) " + constraints + " { return value; }",
            instantiate: true);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false,false)]
    [TestCase(true,false)]
    [TestCase(true,true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.That(program.Functions.Count(function =>
            function.Name.Contains('<', StringComparison.Ordinal)), Is.EqualTo(instantiate ? scalar ? 3 : 2 : 0));
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void ConcreteFloatingCopyDependenciesPreserveOutputModes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public float32 Single;
                public int32 Marker;
                [NoThrow] public Inner(float32 single, int32 marker) { Single = single; Marker = marker; }
            }
            struct Pair {
                public Inner Nested;
                public float64 Wide;
                [NoThrow] public Pair(Inner nested, float64 wide) { Nested = nested; Wide = wide; }
                [NoThrow] public float32 ReadSingle() { return Nested.Single; }
            }
            [NoThrow] bool TryCreatePair(init ref<Pair> result) {
                result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
            [NoThrow] bool TryCreatePairOut(out Pair result) {
                result = new Pair(new Inner(float32(1), 42), float64(2));
                return true;
            }
            [NoThrow] float32 CopyFloat(float32 value) { return value; }
            [NoThrow] T Forward<T>(T value) where T : [Copy] {
                if (!TryCreatePair(init var pair)) { return value; }
                if (!TryCreatePairOut(out var copiedPair)) { return value; }
                if (CopyFloat(pair.ReadSingle()) != float32(1) || pair.Wide != float64(2)
                    || copiedPair.Wide != float64(2)) { return value; }
                return Identity(value);
            }
            [NoThrow] U Identity<U>(U value) where U : [Copy] { return value; }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))().Functions;
        Assert.That(global::System.Linq.Enumerable.Any(functions,static function =>
            function.Name == "TryCreatePair" && function.Variables[0].IsInit),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,static function =>
            function.Name == "TryCreatePairOut" && function.Variables[0].IsOut),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,static function => function.Name == "Forward<int32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,static function => function.Name == "Identity<int32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,static function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }

    [Test]
    public void FloatingManagedClassFieldsRemainOutsideGenericProofHydration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Box {
                float32 _value;
                public Box(float32 value) { _value = value; }
                public static bool TryCreate(out Box result) {
                    result = new Box(float32(1));
                    return true;
                }
                public float32 Read() { return _value; }
            }
            T Forward<T>(T value) where T : [Copy] {
                require Box.TryCreate(out var box);
                if (box.Read() != float32(1)) { return value; }
                return value;
            }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void FloatingManagedClassSignaturesRemainOutsideGenericProofHydration()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Box {
                int32 _value;
                public Box(int32 value) { _value = value; }
                public static bool TryCreate(out Box result) {
                    result = new Box(1);
                    return true;
                }
                public float32 Read() { return float32(_value); }
            }
            T Forward<T>(T value) where T : [Copy] {
                if (!Box.TryCreate(out var box)) { return value; }
                if (box.Read() != float32(1)) { return value; }
                return value;
            }
            int32 Main() { return Forward(int32(42)); }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Value",false)]
    [TestCase("Other",false)]
    [TestCase("Owner",true)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code is "TRM3007" or "TRM3011"),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }

    [Test]
    public void ConcreteWitnessCannotJustifyAnOperationOnAnUnrelatedTypeParameter()
    {
        var compilation = Compile("""
            T Forward<T>(T value) where T : [Copy] {
                return Combine<T, Value>(value, new Value(1));
            }
            A Combine<A, U>(A first, U second)
                where A : Addition<A, A, A>, [Copy] where U : [Copy] { return first; }
            """, instantiate: true);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))().Functions.Length, Is.EqualTo(1024));
    }
    [TestCase("Create()","Value Create() { return new Value(1); }")]
    [TestCase("Value.Create()","")]
    [TestCase("new Outer(new Value(1)).Get()","")]
    [TestCase("Outer.Create().Get()","")]
    [TestCase("created.Get()","")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))().Functions;
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Second<Value, int32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }
    [TestCase(false,false)]
    [TestCase(true,false)]
    [TestCase(false,true)]
    public void InferredHelperResultsRetainConcreteMemberAndOperatorBindings(bool siblingScope, bool add)
    {
        var preceding = siblingScope
            ? "{ var created = Outer.Create(); var unused = created.Get(); }" : "";
        var selected = add
            ? "created.Get() + created.Get()" : "created.Get()";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            interface Addition<A, B, C> { static C operator +(A left, B right); }
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))().Functions;
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Second<Value, int32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false,false,false)]
    [TestCase(false,false,true)]
    [TestCase(true,false,false)]
    [TestCase(true,false,true)]
    [TestCase(true,true,false)]
    [TestCase(true,true,true)]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        Assert.That(program.Classes.Length, Is.EqualTo(2));
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false,"Cell? empty = null;")]
    [TestCase(true,"Cell? empty = null;")]
    [TestCase(false,"write Cell? empty = null;")]
    [TestCase(true,"write Cell? empty = null;")]
    [TestCase(false,"Model.Cell? empty = null;")]
    [TestCase(true,"Model.Cell? empty = null;")]
    [TestCase(false,"write Model.Cell? empty = null;")]
    [TestCase(true,"write Model.Cell? empty = null;")]
    [TestCase(false,"return Second<Model.Cell?, T>(null, value);")]
    [TestCase(true,"return Second<Model.Cell?, T>(null, value);")]
    [TestCase(false,"return Second<write Model.Cell?, T>(null, value);")]
    [TestCase(true,"return Second<write Model.Cell?, T>(null, value);")]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        _ = global::System.Linq.Enumerable.Single(program.Classes);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code is "TRM3007" or "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false,"T.TryCreate(1, out var missing);")]
    [TestCase(true,"T.TryCreate(1, out var missing);")]
    [TestCase(false,"var missing = cell.Secret;")]
    [TestCase(true,"var missing = cell.Secret;")]
    [TestCase(false,"Cell read = cell; read.Set(1);")]
    [TestCase(true,"Cell read = cell; read.Set(1);")]
    [TestCase(false,"Cell? optional = cell; var missing = optional.Value;")]
    [TestCase(true,"Cell? optional = cell; var missing = optional.Value;")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code is "TRM3007" or "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false,true)]
    [TestCase(true,false)]
    [TestCase(true,true)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
            Assert.That(compilation.Program, Is.Null);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))().Functions.Count(
            function => function.Name.Contains('<', StringComparison.Ordinal)), Is.EqualTo(mutual ? 3 : 2));
    }
    [TestCase(16)]
    [TestCase(17)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.That(((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))().Functions.Count(
                function => function.Name.Contains('<', StringComparison.Ordinal)), Is.EqualTo(32));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
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
