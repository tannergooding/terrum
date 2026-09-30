// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolControlFlowTests
{
    private const string Declarations = """
        interface Multiplication<A, B, C> {
            static C operator *(A left, B right);
        }
        struct Meters : Multiplication<Meters, Meters, Meters> {
            public int32 Value;
            public Meters(int32 value) { Value = value; }
            public static Meters operator *(Meters left, Meters right) {
                return new Meters(left.Value * right.Value);
            }
            public static Meters operator +(Meters left, Meters right) {
                return new Meters(left.Value + right.Value);
            }
        }
        """;

    private const string PowerBody = """
        mut T result = one;
        mut T factor = value;
        mut uint32 remaining = exponent;
        while (remaining != 0) {
            if ((remaining & 1) != 0) { result = result * factor; }
            remaining = remaining / 2;
            if (remaining == 0) { break; }
            factor = factor * factor;
        }
        return result;
        """;

    private const string NoThrowDeclarations = """
        interface Multiplication<A, B, C> {
            [NoThrow] static C operator *(A left, B right);
        }
        """;

    [Fact]
    public void StructuredTemplateUsesScalarFlowAndOnlyDeclaredOperators()
    {
        var compilation = Compile(PowerBody, instantiate: true);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var specialization = Assert.Single(program.Functions,
            function => function.Name.StartsWith("Power<", StringComparison.Ordinal));
        Assert.Contains(specialization.Body.Statements, statement => statement is BoundWhile);
        Assert.DoesNotContain(program.Structures, structure => structure.Name == "$T");
        Assert.DoesNotContain(program.Functions, function => function.Name.Contains("$T", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnusedStructuredTemplateIsCheckedWithoutAConcreteWitness()
    {
        var compilation = Compile(PowerBody, instantiate: false);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.DoesNotContain(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name.StartsWith("Power<", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedForContinueAndRequireBindAtDefinitionAndSpecialization(bool instantiate)
    {
        var compilation = Compile("""
            mut T result = one;
            for (mut uint32 i = 0; i < exponent; i = i + 1) {
                if (i == 0) { continue; }
                else { result = result * value; }
            }
            require (exponent < 100);
            return result;
            """, instantiate);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("return value + one;", "TRM3007")]
    [InlineData("return value.Value;", "TRM3007")]
    [InlineData("return new T();", "TRM3007")]
    [InlineData("mut uint32 count = exponent; count = value; return one;", "TRM3007")]
    [InlineData("uint32 count = exponent; count = 0; return one;", "TRM3008")]
    [InlineData("mut T result = value; mut T result = one; return result;", "TRM3004")]
    [InlineData("if (value) { return one; } return value;", "TRM3007")]
    [InlineData("{ mut T local = value; } return local;", "TRM3006")]
    [InlineData("break; return one;", "TRM3008")]
    [InlineData("if (exponent == 0) { return one; }", "TRM3012")]
    [InlineData("return Power<Meters>(value, one, exponent);", "TRM3007")]
    [InlineData("require (exponent == 0); return Missing(value);", "TRM3011")]
    [InlineData("mut T result = one; for (mut uint32 i = 0; i < exponent; i = i + 1) { if (i == 0) { continue; } result = result / value; } return result;", "TRM3007")]
    public void InvalidDefinitionRejectsEvenWithoutInstantiation(string body, string expected)
    {
        var compilation = Compile(body, instantiate: false);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void InvalidDefinitionCannotBeRescuedByConformingConcreteType()
    {
        var compilation = Compile("return value + one;", instantiate: true);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Fact]
    public void InvalidDefinitionIsReportedOnItsSourceSnapshot()
    {
        var tree = SyntaxTree.Parse(Declarations + Generic("return value + one;") + "int32 Main() { return 0; }",
            "invalid-template.terrum");
        var compilation = Compilation.Create(tree);
        Assert.Contains(compilation.SourceDiagnostics, item => item.Diagnostic.Code == "TRM3007"
            && item.SyntaxTree == tree);
    }

    [Fact]
    public void NoThrowStructuredDefinitionRejectsScalarFailuresWithoutInstantiation()
    {
        var source = NoThrowDeclarations + "[NoThrow] " + Generic("""
            mut uint32 half = exponent / 2;
            if (half == 0) { return one; }
            return value * one;
            """) + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
    }

    [Fact]
    public void NoThrowStructuredDefinitionAcceptsOnlyConstrainedNoThrowCalls()
    {
        var source = NoThrowDeclarations + "[NoThrow] " + Generic("""
            if (exponent == 0) { return one; }
            return value * one;
            """) + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static Compilation Compile(string body, bool instantiate)
    {
        var main = instantiate
            ? "int32 Main() { return Power<Meters>(new Meters(2), new Meters(1), 3).Value + 34; }"
            : "int32 Main() { return 0; }";
        return Compilation.Create(SyntaxTree.Parse(Declarations + Generic(body) + main));
    }

    private static string Generic(string body) =>
        "T Power<T>(T value, T one, uint32 exponent) where T : Multiplication<T, T, T>, [Copy] {"
        + body + "}";
}
