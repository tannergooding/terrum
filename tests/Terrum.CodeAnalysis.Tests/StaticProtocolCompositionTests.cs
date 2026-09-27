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
    [InlineData("return Double(value, true);", "Addition<T, T, T>", "TRM3011")]
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

    [Fact]
    public void SpecializationResolvesEachHelpersOwnNamespaceAndImports()
    {
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace Contracts;" + Protocols + Value),
            SyntaxTree.Parse("""
                using Contracts;
                using Helpers;
                namespace Callers;
                T Compose<T>(T value) where T : Addition<T, T, T>, [Copy] {
                    return Double<T>(value, true);
                }
                int32 Main() { return Compose<Value>(new Value(3)).Number; }
                """),
            SyntaxTree.Parse("""
                using Contracts;
                using Leaves;
                namespace Helpers;
                U Double<U>(U value, bool twice) where U : Addition<U, U, U>, [Copy] {
                    if (twice) { return Identity<U>(value + value); }
                    return Identity<U>(value);
                }
                """),
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
